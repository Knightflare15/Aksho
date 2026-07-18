import assert from "node:assert/strict";
import test from "node:test";
import { safelyBeginCheckout } from "../src/services/checkoutFlow.js";
import { createHostedCheckoutProvider, validateHostedUrl } from "../src/services/subscriptionProviderCore.js";
import type { SubscriptionProvider } from "../src/services/subscriptionTypes.js";

const baseUrl = "https://portal.example.test";

test("an individual tier checkout returns a verified HTTPS destination", async () => {
  const provider = createHostedCheckoutProvider({
    providerId: "hosted-checkout",
    baseUrl,
    checkoutUrls: { "individual-plus": "https://billing.example.test/individual-plus" }
  });

  assert.equal(provider.isConfigured, true);
  assert.equal(provider.isPlanConfigured("individual-plus"), true);
  assert.equal(provider.isPlanConfigured("institution"), false);
  assert.deepEqual(await provider.beginCheckout("individual-plus"), {
    status: "ready",
    url: "https://billing.example.test/individual-plus"
  });
});

test("an unconfigured provider keeps tier checkout pending and never claims a charge", async () => {
  const provider = createHostedCheckoutProvider({
    providerId: "not-configured",
    baseUrl,
    checkoutUrls: { "individual-plus": "https://billing.example.test/individual-plus" }
  });

  assert.equal(provider.isConfigured, false);
  const result = await provider.beginCheckout("individual-plus");
  assert.equal(result.status, "unavailable");
  assert.match(result.message, /not connected/i);
  assert.match(result.message, /not been charged/i);
});

test("a missing individual tier link is an explicit unavailable state", async () => {
  const provider = createHostedCheckoutProvider({
    providerId: "hosted-checkout",
    baseUrl,
    checkoutUrls: { institution: "https://billing.example.test/institution" }
  });

  const result = await provider.beginCheckout("individual-plus");
  assert.equal(provider.isConfigured, true, "The institution destination still configures the provider globally.");
  assert.equal(provider.isPlanConfigured("institution"), true);
  assert.equal(provider.isPlanConfigured("individual-plus"), false);
  assert.equal(result.status, "unavailable");
  assert.match(result.message, /individual-plus checkout is not available/i);
});

test("unsafe checkout schemes are rejected", async () => {
  const provider = createHostedCheckoutProvider({
    providerId: "hosted-checkout",
    baseUrl,
    checkoutUrls: { "individual-plus": "javascript:alert('charge')" }
  });

  const result = await provider.beginCheckout("individual-plus");
  assert.equal(result.status, "unavailable");
  assert.match(result.message, /configuration is invalid/i);
});

test("mailto is accepted for institution contact only", () => {
  assert.equal(validateHostedUrl("mailto:sales@example.test", baseUrl, true), "mailto:sales@example.test");
  assert.equal(validateHostedUrl("mailto:sales@example.test", baseUrl, false), null);
});

test("local HTTP checkout is allowed for development but remote HTTP is rejected", () => {
  assert.equal(validateHostedUrl("http://localhost:4242/checkout", baseUrl, false), "http://localhost:4242/checkout");
  assert.equal(validateHostedUrl("http://billing.example.test/checkout", baseUrl, false), null);
});

test("an injected provider exception becomes a no-charge unavailable result", async () => {
  const throwingProvider: SubscriptionProvider = {
    id: "future-provider",
    isConfigured: true,
    isPlanConfigured: () => true,
    async beginCheckout() {
      throw new Error("provider outage");
    }
  };

  const result = await safelyBeginCheckout(throwingProvider, "individual-plus");
  assert.equal(result.status, "unavailable");
  assert.match(result.message, /could not be opened/i);
  assert.match(result.message, /not been charged/i);
});

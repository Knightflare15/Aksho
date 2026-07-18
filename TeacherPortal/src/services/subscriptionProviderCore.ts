import type {
  CheckoutResult,
  HostedCheckoutConfig,
  SubscriptionPlanId,
  SubscriptionProvider
} from "./subscriptionTypes.js";

const HOSTED_PROVIDER_ID = "hosted-checkout";

export function createHostedCheckoutProvider(config: HostedCheckoutConfig): SubscriptionProvider {
  const id = config.providerId?.trim() || "not-configured";
  const checkoutUrls = config.checkoutUrls ?? {};
  const isPlanConfigured = (planId: SubscriptionPlanId): boolean => {
    if (id !== HOSTED_PROVIDER_ID) return false;

    const rawUrl = checkoutUrls[planId]?.trim();
    return Boolean(rawUrl && validateHostedUrl(rawUrl, config.baseUrl, planId === "institution"));
  };

  return {
    id,
    isConfigured: (["individual-starter", "individual-plus", "individual-family", "institution"] as SubscriptionPlanId[]).some(isPlanConfigured),
    isPlanConfigured,
    async beginCheckout(planId: SubscriptionPlanId): Promise<CheckoutResult> {
      if (id !== HOSTED_PROVIDER_ID) {
        return {
          status: "unavailable",
          message: "Secure checkout is not connected on this deployment yet. Your account has not been charged."
        };
      }

      const rawUrl = checkoutUrls[planId]?.trim();
      if (!rawUrl) {
        return {
          status: "unavailable",
          message: `The ${planId} checkout is not available yet. Your account has not been charged.`
        };
      }

      const safeUrl = validateHostedUrl(rawUrl, config.baseUrl, planId === "institution");
      if (!safeUrl) {
        return {
          status: "unavailable",
          message: "Checkout configuration is invalid. Your account has not been charged."
        };
      }

      return { status: "ready", url: safeUrl };
    }
  };
}

export function validateHostedUrl(rawUrl: string, baseUrl: string, allowMailTo: boolean): string | null {
  try {
    const url = new URL(rawUrl, baseUrl);
    const localHttp = url.protocol === "http:" && (url.hostname === "localhost" || url.hostname === "127.0.0.1");
    if (url.protocol === "https:" || localHttp || (allowMailTo && url.protocol === "mailto:")) {
      return url.toString();
    }
  } catch {
    return null;
  }

  return null;
}

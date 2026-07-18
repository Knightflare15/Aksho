import type { CheckoutResult, SubscriptionPlanId, SubscriptionProvider } from "./subscriptionTypes.js";

const CHECKOUT_FAILURE_MESSAGE = "Secure checkout could not be opened. Your account has not been charged. Please try again later.";

export async function safelyBeginCheckout(
  provider: SubscriptionProvider,
  planId: SubscriptionPlanId
): Promise<CheckoutResult> {
  try {
    return await provider.beginCheckout(planId);
  } catch {
    return {
      status: "unavailable",
      message: CHECKOUT_FAILURE_MESSAGE
    };
  }
}

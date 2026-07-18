import { createHostedCheckoutProvider } from "./subscriptionProviderCore";

export const subscriptionProvider = createHostedCheckoutProvider({
  providerId: import.meta.env.VITE_BILLING_PROVIDER,
  baseUrl: window.location.origin,
  checkoutUrls: {
    "individual-starter": import.meta.env.VITE_BILLING_INDIVIDUAL_STARTER_CHECKOUT_URL ?? import.meta.env.VITE_BILLING_INDIVIDUAL_CHECKOUT_URL,
    "individual-plus": import.meta.env.VITE_BILLING_INDIVIDUAL_PLUS_CHECKOUT_URL ?? import.meta.env.VITE_BILLING_INDIVIDUAL_CHECKOUT_URL,
    "individual-family": import.meta.env.VITE_BILLING_INDIVIDUAL_FAMILY_CHECKOUT_URL ?? import.meta.env.VITE_BILLING_INDIVIDUAL_CHECKOUT_URL,
    institution: import.meta.env.VITE_BILLING_INSTITUTION_CONTACT_URL ?? import.meta.env.VITE_BILLING_SCHOOL_CONTACT_URL ?? import.meta.env.VITE_BILLING_EDUCATOR_CHECKOUT_URL
  }
});

export type {
  CheckoutResult,
  SubscriptionPlan,
  SubscriptionPlanId,
  SubscriptionProvider
} from "./subscriptionTypes";

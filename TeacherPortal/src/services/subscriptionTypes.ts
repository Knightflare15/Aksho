export type SubscriptionPlanId =
  | "individual-starter"
  | "individual-plus"
  | "individual-family"
  | "institution";

export interface SubscriptionPlan {
  id: SubscriptionPlanId;
  name: string;
  audience: string;
  price: string;
  cadence?: string;
  description: string;
  features: string[];
  featured?: boolean;
  actionLabel: string;
}

export type CheckoutResult =
  | { status: "ready"; url: string }
  | { status: "unavailable"; message: string };

export interface SubscriptionProvider {
  readonly id: string;
  readonly isConfigured: boolean;
  isPlanConfigured(planId: SubscriptionPlanId): boolean;
  beginCheckout(planId: SubscriptionPlanId): Promise<CheckoutResult>;
}

export interface HostedCheckoutConfig {
  providerId?: string;
  checkoutUrls?: Partial<Record<SubscriptionPlanId, string>>;
  baseUrl: string;
}

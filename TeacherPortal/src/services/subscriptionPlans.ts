import type { SubscriptionPlan } from "./subscriptionTypes";

const price = (key: string, fallback: string) => {
  const value = import.meta.env[key];
  return typeof value === "string" && value.trim() ? value.trim() : fallback;
};

export const subscriptionPlans: SubscriptionPlan[] = [
  {
    id: "individual-starter",
    name: "Individual Starter",
    audience: "For one child",
    price: price("VITE_PRICE_INDIVIDUAL_STARTER_LABEL", price("VITE_PRICE_INDIVIDUAL_LABEL", "₹399")),
    cadence: "/ month",
    description: "A focused home plan for one learner getting started with grammar adventures.",
    features: ["1 learner profile", "Daily grammar adventures", "Adult progress summaries", "Core pronunciation feedback"],
    actionLabel: "Choose starter"
  },
  {
    id: "individual-plus",
    name: "Individual Plus",
    audience: "For steady home practice",
    price: price("VITE_PRICE_INDIVIDUAL_PLUS_LABEL", "₹699"),
    cadence: "/ month",
    description: "More feedback and richer review for a child practicing consistently at home.",
    features: ["1 learner profile", "Expanded Buddy practice", "Speaking and handwriting feedback", "Weekly adult insight summaries"],
    featured: true,
    actionLabel: "Choose plus"
  },
  {
    id: "individual-family",
    name: "Individual Family",
    audience: "For siblings",
    price: price("VITE_PRICE_INDIVIDUAL_FAMILY_LABEL", "₹999"),
    cadence: "/ month",
    description: "A family tier for multiple children with one adult progress view.",
    features: ["Up to 3 learner profiles", "Shared family progress view", "Personalized revision", "Priority home support"],
    actionLabel: "Choose family"
  },
  {
    id: "institution",
    name: "School / Educator",
    audience: "For classrooms and schools",
    price: price("VITE_PRICE_INSTITUTION_LABEL", price("VITE_PRICE_SCHOOL_LABEL", "Let's talk")),
    description: "One institutional route for teachers, classrooms, and school rollouts.",
    features: ["Teacher workspace", "Classroom or school access", "Class and learner reports", "Privacy, consent, and rollout support"],
    actionLabel: "Contact for access"
  }
];

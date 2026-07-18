import {
  onAuthStateChanged,
  sendPasswordResetEmail,
  signInWithEmailAndPassword,
  signOut,
  type User
} from "firebase/auth";
import { auth, hasFirebaseConfig } from "../firebase";

export interface PortalAuthSession {
  userId: string;
  email?: string;
}

export interface PortalAuthProvider {
  readonly id: string;
  readonly isConfigured: boolean;
  subscribe(listener: (session: PortalAuthSession | null) => void): () => void;
  signIn(email: string, password: string): Promise<void>;
  signOut(): Promise<void>;
  sendPasswordReset(email: string): Promise<void>;
  describeError(error: unknown): string;
}

const AUTH_TIMEOUT_MS = 15_000;

class FirebasePortalAuthProvider implements PortalAuthProvider {
  readonly id = "firebase";
  readonly isConfigured = hasFirebaseConfig && Boolean(auth);

  subscribe(listener: (session: PortalAuthSession | null) => void): () => void {
    if (!auth) {
      listener(null);
      return () => undefined;
    }

    return onAuthStateChanged(auth, (user) => listener(toSession(user)));
  }

  async signIn(email: string, password: string): Promise<void> {
    if (!auth) {
      throw new Error("Production sign-in is not configured for this deployment.");
    }

    await withTimeout(
      signInWithEmailAndPassword(auth, email.trim(), password).then(() => undefined),
      "Sign-in is taking too long. Check your connection and try again."
    );
  }

  async signOut(): Promise<void> {
    if (auth) {
      await signOut(auth);
    }
  }

  async sendPasswordReset(email: string): Promise<void> {
    if (!auth) {
      throw new Error("Password reset is not configured for this deployment.");
    }

    await withTimeout(
      sendPasswordResetEmail(auth, email.trim()),
      "Password reset is taking too long. Check your connection and try again."
    );
  }

  describeError(error: unknown): string {
    const message = error instanceof Error ? error.message : "Sign in failed.";
    if (
      message.includes("auth/invalid-credential") ||
      message.includes("auth/wrong-password") ||
      message.includes("auth/user-not-found")
    ) {
      return "That email and password do not match an active account.";
    }
    if (message.includes("auth/invalid-email")) {
      return "Enter a valid email address.";
    }
    if (message.includes("auth/too-many-requests")) {
      return "Too many attempts. Wait a minute, then try again or reset your password.";
    }
    if (message.includes("auth/network-request-failed")) {
      return "We could not reach the sign-in service. Check your connection and try again.";
    }
    return message;
  }
}

function toSession(user: User | null): PortalAuthSession | null {
  if (!user) {
    return null;
  }

  return {
    userId: user.uid,
    email: user.email ?? undefined
  };
}

async function withTimeout<T>(promise: Promise<T>, message: string): Promise<T> {
  let timeoutId: number | undefined;
  const timeout = new Promise<never>((_, reject) => {
    timeoutId = window.setTimeout(() => reject(new Error(message)), AUTH_TIMEOUT_MS);
  });

  try {
    return await Promise.race([promise, timeout]);
  } finally {
    if (timeoutId !== undefined) {
      window.clearTimeout(timeoutId);
    }
  }
}

export const portalAuthProvider: PortalAuthProvider = new FirebasePortalAuthProvider();

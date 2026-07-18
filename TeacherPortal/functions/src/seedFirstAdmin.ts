import { initializeApp } from "firebase-admin/app";
import { getAuth, type UserRecord } from "firebase-admin/auth";
import { FieldValue, getFirestore } from "firebase-admin/firestore";

interface SeedConfig {
  projectId: string;
  adminEmail: string;
  adminPassword: string;
  adminDisplayName: string;
  schoolId: string;
  schoolName: string;
  academicYear: string;
}

const args = parseArgs(process.argv.slice(2));
const config = readConfig(args);

initializeApp({ projectId: config.projectId });

const auth = getAuth();
const db = getFirestore();

const user = await getOrCreateAdminUser(config);
await auth.setCustomUserClaims(user.uid, {
  role: "admin",
  schoolId: config.schoolId,
  classIds: [],
  studentIds: []
});

await db.doc(`schools/${config.schoolId}`).set({
  id: config.schoolId,
  name: config.schoolName,
  academicYear: config.academicYear,
  updatedAt: FieldValue.serverTimestamp(),
  createdAt: FieldValue.serverTimestamp()
}, { merge: true });

await db.doc(`users/${user.uid}`).set({
  uid: user.uid,
  email: config.adminEmail,
  displayName: config.adminDisplayName,
  role: "admin",
  schoolId: config.schoolId,
  classIds: [],
  studentIds: [],
  updatedAt: FieldValue.serverTimestamp(),
  createdAt: FieldValue.serverTimestamp()
}, { merge: true });

await db.collection("auditEvents").add({
  schoolId: config.schoolId,
  actorUid: user.uid,
  action: "role.seedFirstAdmin",
  targetPath: `users/${user.uid}`,
  createdAt: FieldValue.serverTimestamp()
});

console.log("First admin seeded successfully.");
console.log(`Project: ${config.projectId}`);
console.log(`School: ${config.schoolName} (${config.schoolId})`);
console.log(`Admin: ${config.adminEmail}`);
console.log(`UID: ${user.uid}`);

async function getOrCreateAdminUser(seedConfig: SeedConfig): Promise<UserRecord> {
  try {
    const existing = await auth.getUserByEmail(seedConfig.adminEmail);
    await auth.updateUser(existing.uid, {
      displayName: seedConfig.adminDisplayName,
      disabled: false
    });
    return auth.getUser(existing.uid);
  } catch (error) {
    if (!isAuthUserNotFound(error)) {
      throw error;
    }
    if (!seedConfig.adminPassword) {
      throw new Error("ADMIN_PASSWORD or --adminPassword is required when the admin Auth user does not already exist.");
    }

    return auth.createUser({
      email: seedConfig.adminEmail,
      password: seedConfig.adminPassword,
      displayName: seedConfig.adminDisplayName,
      emailVerified: true,
      disabled: false
    });
  }
}

function readConfig(values: Record<string, string>): SeedConfig {
  const projectId = values.projectId || env("FIREBASE_PROJECT_ID") || env("GCLOUD_PROJECT") || env("GOOGLE_CLOUD_PROJECT");
  const adminEmail = values.adminEmail || env("ADMIN_EMAIL");
  const adminPassword = values.adminPassword || env("ADMIN_PASSWORD");
  const adminDisplayName = values.adminDisplayName || env("ADMIN_DISPLAY_NAME") || "School Admin";
  const schoolId = values.schoolId || env("SCHOOL_ID") || "pilot-school";
  const schoolName = values.schoolName || env("SCHOOL_NAME") || "Pilot School";
  const academicYear = values.academicYear || env("ACADEMIC_YEAR") || "2026-2027";

  if (!projectId) {
    throw new Error("FIREBASE_PROJECT_ID or --projectId is required.");
  }
  if (!adminEmail) {
    throw new Error("ADMIN_EMAIL or --adminEmail is required.");
  }

  return {
    projectId,
    adminEmail,
    adminPassword,
    adminDisplayName,
    schoolId,
    schoolName,
    academicYear
  };
}

function parseArgs(argv: string[]): Record<string, string> {
  const result: Record<string, string> = {};
  for (const arg of argv) {
    if (!arg.startsWith("--")) {
      continue;
    }

    const separator = arg.indexOf("=");
    if (separator < 0) {
      result[arg.slice(2)] = "true";
      continue;
    }

    result[arg.slice(2, separator)] = arg.slice(separator + 1);
  }
  return result;
}

function env(name: string) {
  return process.env[name] ?? "";
}

function isAuthUserNotFound(error: unknown) {
  return typeof error === "object"
    && error !== null
    && "code" in error
    && (error as { code?: string }).code === "auth/user-not-found";
}

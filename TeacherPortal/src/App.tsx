import { BarChart3, CalendarDays, FileText, GraduationCap, RefreshCw, Sparkles, Users } from "lucide-react";
import { useEffect, useState } from "react";
import TacticalCombatVisualizer from "./combatLab/TacticalCombatVisualizer";
import PortalFrame, { type PortalNavigationItem } from "./components/PortalFrame";
import PublicWebsite from "./PublicWebsite";
import { portalAuthProvider } from "./services/authProvider";
import { buildDemoDataset, loadPortalDataset, loadUserProfile, makeDemoProfile, type PortalDataset } from "./portalData";
import type { UserProfile } from "./types";
import { AdminDashboard } from "./pages/AdminDashboard";
import { ParentDashboard } from "./pages/ParentDashboard";
import { StudentDashboard } from "./pages/StudentDashboard";
import { TeacherDashboard, type TeacherTab } from "./pages/TeacherDashboard";
import { isPublicMarketingPath } from "./utils/portalTail";

export default function App() {
  if (window.location.pathname === "/combat-lab" || window.location.search.includes("combatLab=1")) {
    return <TacticalCombatVisualizer />;
  }

  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [dataset, setDataset] = useState<PortalDataset | null>(null);
  const [loading, setLoading] = useState(portalAuthProvider.isConfigured);
  const [authError, setAuthError] = useState("");
  const [isDemoSession, setIsDemoSession] = useState(false);
  const [teacherTab, setTeacherTab] = useState<TeacherTab>("overview");
  const [, setLocationVersion] = useState(0);

  useEffect(() => {
    const handlePopState = () => setLocationVersion((version) => version + 1);
    window.addEventListener("popstate", handlePopState);
    return () => window.removeEventListener("popstate", handlePopState);
  }, []);

  useEffect(() => {
    if (!portalAuthProvider.isConfigured) {
      setLoading(false);
      return;
    }

    const unsubscribe = portalAuthProvider.subscribe(async (session) => {
      setLoading(true);
      setAuthError("");
      try {
        if (!session) {
          setProfile(null);
          setDataset(null);
          setLoading(false);
          return;
        }

        const nextProfile = await withTimeout(
          loadUserProfile(session.userId),
          15000,
          "Timed out loading your user profile from Firestore."
        );
        if (!nextProfile) {
          setAuthError("This Firebase account has no production user profile yet.");
          setProfile(null);
          setDataset(null);
          setLoading(false);
          return;
        }

        setProfile(nextProfile);
        setDataset(await withTimeout(
          loadPortalDataset(nextProfile),
          15000,
          "Timed out loading the school workspace from Firestore."
        ));
      } catch (error) {
        setProfile(null);
        setDataset(null);
        setAuthError(error instanceof Error ? error.message : "Could not load portal data.");
      } finally {
        setLoading(false);
      }
    });

    return unsubscribe;
  }, []);

  const startDemo = (role: UserProfile["role"]) => {
    const demoProfile = makeDemoProfile(role);
    window.history.replaceState({}, "", "/portal");
    setIsDemoSession(true);
    setProfile(demoProfile);
    setDataset(buildDemoDataset(demoProfile));
    setAuthError("");
    resetPageScrollAfterViewChange();
  };

  const refresh = async () => {
    if (!profile) {
      return;
    }
    setLoading(true);
    try {
      setDataset(isDemoSession ? buildDemoDataset(profile) : await loadPortalDataset(profile));
    } catch (error) {
      setAuthError(error instanceof Error ? error.message : "Could not refresh portal data.");
    } finally {
      setLoading(false);
    }
  };

  const logout = async () => {
    try {
      if (!isDemoSession) {
        await portalAuthProvider.signOut();
      }
    } finally {
      window.history.replaceState({}, "", "/login");
      setProfile(null);
      setDataset(null);
      setIsDemoSession(false);
      resetPageScrollAfterViewChange();
    }
  };

  const openPortal = () => {
    window.history.pushState({}, "", "/portal");
    setLocationVersion((version) => version + 1);
    resetPageScrollAfterViewChange();
  };

  if (loading) {
    return (
      <main className="loginShell">
        <section className="loginPanel compactLogin">
          <RefreshCw className="spin" size={26} />
          <h1>Loading school workspace</h1>
          <p className="muted">Checking role, class membership, and available student reports.</p>
        </section>
      </main>
    );
  }

  const publicPath = isPublicMarketingPath(window.location.pathname);
  if (publicPath || !profile || !dataset) {
    return (
      <PublicWebsite
        authError={authError}
        signedIn={Boolean(profile && dataset)}
        onDemo={startDemo}
        onOpenPortal={openPortal}
      />
    );
  }

  if (profile.role === "parent") {
    return <PortalFrame profile={profile} dataset={dataset} onLogout={logout} onRefresh={refresh} isDemoSession={isDemoSession}>
      <ParentDashboard dataset={dataset} />
    </PortalFrame>;
  }

  if (profile.role === "student") {
    return <PortalFrame profile={profile} dataset={dataset} onLogout={logout} onRefresh={refresh} isDemoSession={isDemoSession}>
      <StudentDashboard dataset={dataset} />
    </PortalFrame>;
  }

  if (profile.role === "admin") {
    return <PortalFrame profile={profile} dataset={dataset} onLogout={logout} onRefresh={refresh} isDemoSession={isDemoSession}>
      <AdminDashboard profile={profile} dataset={dataset} setDataset={setDataset} />
    </PortalFrame>;
  }

  const teacherNavigation: PortalNavigationItem[] = [
    { id: "overview", label: "Classroom pulse", icon: <Users size={18} /> },
    { id: "classes", label: "Learners & classes", icon: <GraduationCap size={18} /> },
    { id: "mission", label: "Learning plan", icon: <CalendarDays size={18} /> },
    { id: "reports", label: "Learner reports", icon: <BarChart3 size={18} /> },
    { id: "handwriting", label: "Evidence library", icon: <FileText size={18} /> },
    { id: "assistant", label: "AI assistant", icon: <Sparkles size={18} /> }
  ];

  return <PortalFrame
    profile={profile}
    dataset={dataset}
    onLogout={logout}
    onRefresh={refresh}
    isDemoSession={isDemoSession}
    navigation={teacherNavigation}
    activeNavigation={teacherTab}
    onNavigate={(id) => setTeacherTab(id as TeacherTab)}
  >
    <TeacherDashboard profile={profile} dataset={dataset} setDataset={setDataset} tab={teacherTab} setTab={setTeacherTab} />
  </PortalFrame>;
}

function resetPageScrollAfterViewChange() {
  window.scrollTo({ top: 0, left: 0, behavior: "auto" });
  window.requestAnimationFrame(() => {
    window.scrollTo({ top: 0, left: 0, behavior: "auto" });
  });
}

async function withTimeout<T>(promise: Promise<T>, timeoutMs: number, message: string): Promise<T> {
  let timeoutId: number | undefined;
  const timeout = new Promise<never>((_, reject) => {
    timeoutId = window.setTimeout(() => reject(new Error(message)), timeoutMs);
  });
  try {
    return await Promise.race([promise, timeout]);
  } finally {
    if (timeoutId !== undefined) {
      window.clearTimeout(timeoutId);
    }
  }
}

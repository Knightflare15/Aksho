import { FileText, GraduationCap, LogOut, RefreshCw, ShieldCheck, Users } from "lucide-react";
import type { ReactNode } from "react";
import type { PortalDataset } from "../portalData";
import type { UserProfile } from "../types";

export interface PortalNavigationItem {
  id: string;
  label: string;
  icon: ReactNode;
}

interface PortalFrameProps {
  profile: UserProfile;
  dataset: PortalDataset;
  isDemoSession: boolean;
  onRefresh: () => void;
  onLogout: () => void;
  navigation?: PortalNavigationItem[];
  activeNavigation?: string;
  onNavigate?: (id: string) => void;
  children: ReactNode;
}

export default function PortalFrame(props: PortalFrameProps) {
  return (
    <main className="appShell">
      <aside className="sidebar">
        <div className="portalBrand">
          <span className="portalBrandMark"><GraduationCap size={20} /></span>
          <span><strong>The Script</strong><small>Learning workspace</small></span>
        </div>
        <div className="schoolIdentity">
          <span className="schoolAvatar" aria-hidden="true">{props.dataset.school.name.slice(0, 1).toUpperCase()}</span>
          <div><strong>{props.dataset.school.name}</strong><small>{props.dataset.activeClass?.name ?? roleLabel(props.profile.role)}</small></div>
        </div>
        <nav aria-label="Workspace navigation">
          <p className="sidebarLabel">Workspace</p>
          {props.navigation?.length ? props.navigation.map((item) => (
            <button
              type="button"
              key={item.id}
              className={props.activeNavigation === item.id ? "navButton active" : "navButton"}
              aria-current={props.activeNavigation === item.id ? "page" : undefined}
              onClick={() => props.onNavigate?.(item.id)}
            >
              {item.icon}
              <span>{item.label}</span>
            </button>
          )) : (
            <div className="navButton active">
              {props.profile.role === "admin" ? <ShieldCheck size={18} /> : props.profile.role === "parent" ? <FileText size={18} /> : <Users size={18} />}
              {roleLabel(props.profile.role)}
            </div>
          )}
        </nav>
        <div className="sidebarBottom">
          <a className="sidebarPlanCard" href="/pricing">
            <span>Plans & access</span>
            <strong>{props.isDemoSession ? "Demo workspace" : "Account settings"}</strong>
            <small>Review individual tiers and School / Educator access.</small>
          </a>
          <div className="sidebarProfile">
            <span className="profileAvatar">{initials(props.profile.displayName)}</span>
            <div><strong>{props.profile.displayName}</strong><small>{roleLabel(props.profile.role)}</small></div>
            {props.isDemoSession && <span className="demoDot" title="Demo session" />}
          </div>
          <div className="sidebarActions">
            <button className="secondaryButton" type="button" onClick={props.onRefresh}>
              <RefreshCw size={17} />
              Refresh
            </button>
            <button className="secondaryButton" type="button" onClick={props.onLogout}>
              <LogOut size={17} />
              Sign Out
            </button>
          </div>
        </div>
      </aside>
      <section className="content">{props.children}</section>
    </main>
  );
}

function roleLabel(role: UserProfile["role"]) {
  return role === "admin" ? "Admin" : role === "parent" ? "Parent" : role === "student" ? "Student" : "Teacher";
}

function initials(displayName: string) {
  return displayName
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join("") || "TS";
}

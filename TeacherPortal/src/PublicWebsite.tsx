import {
  ArrowRight,
  BarChart3,
  BookOpenCheck,
  Check,
  ChevronRight,
  ClipboardList,
  Gamepad2,
  GraduationCap,
  HeartHandshake,
  LockKeyhole,
  Mail,
  Play,
  RefreshCw,
  ShieldCheck,
  Sparkles,
  Target,
  UserRound,
  Users
} from "lucide-react";
import { useEffect, useMemo, useState, type FormEvent, type MouseEvent, type ReactNode } from "react";
import { portalAuthProvider } from "./services/authProvider";
import { safelyBeginCheckout } from "./services/checkoutFlow";
import { subscriptionPlans } from "./services/subscriptionPlans";
import { subscriptionProvider, type SubscriptionPlanId } from "./services/subscriptionProvider";
import type { UserRole } from "./types";
import CheckoutNotice from "./components/CheckoutNotice";

type PublicPage = "home" | "pricing" | "login" | "subscribe";

interface PublicWebsiteProps {
  authError?: string;
  signedIn?: boolean;
  onOpenPortal: () => void;
  onDemo: (role: UserRole) => void;
}

export default function PublicWebsite(props: PublicWebsiteProps) {
  const [page, setPage] = useState<PublicPage>(() => pageFromLocation());
  const [selectedPlan, setSelectedPlan] = useState<SubscriptionPlanId>(() => planFromLocation());
  const [checkoutStatus, setCheckoutStatus] = useState("");
  const [checkoutBusy, setCheckoutBusy] = useState<SubscriptionPlanId | null>(null);

  useEffect(() => {
    const handlePopState = () => {
      setPage(pageFromLocation());
      setSelectedPlan(planFromLocation());
      window.scrollTo({ top: 0, left: 0, behavior: "auto" });
    };
    window.addEventListener("popstate", handlePopState);
    return () => window.removeEventListener("popstate", handlePopState);
  }, []);

  const navigate = (nextPage: PublicPage, planId?: SubscriptionPlanId) => {
    const path = pathForPage(nextPage, planId);
    window.history.pushState({}, "", path);
    setPage(nextPage);
    if (planId) {
      setSelectedPlan(planId);
    }
    setCheckoutStatus("");
    window.scrollTo({ top: 0, left: 0, behavior: "auto" });
  };

  const startCheckout = async (planId: SubscriptionPlanId) => {
    setCheckoutBusy(planId);
    setCheckoutStatus("");
    try {
      const result = await safelyBeginCheckout(subscriptionProvider, planId);
      if (result.status === "ready") {
        window.location.assign(result.url);
        return;
      }
      setCheckoutStatus(result.message);
      if (page !== "subscribe") {
        setSelectedPlan(planId);
      }
    } catch {
      setCheckoutStatus("Secure checkout could not be opened. Your account has not been charged. Please try again later.");
    } finally {
      setCheckoutBusy(null);
    }
  };

  return (
    <div className="publicSite">
      <a className="skipLink" href="#main-content">Skip to content</a>
      <MarketingHeader
        page={page}
        signedIn={props.signedIn}
        navigate={navigate}
        onOpenPortal={props.onOpenPortal}
      />
      <main id="main-content">
        {page === "home" && (
          <HomePage
            signedIn={props.signedIn}
            navigate={navigate}
            onOpenPortal={props.onOpenPortal}
          />
        )}
        {page === "pricing" && (
          <PricingPage
            checkoutBusy={checkoutBusy}
            checkoutStatus={checkoutStatus}
            navigate={navigate}
            startCheckout={startCheckout}
          />
        )}
        {page === "login" && (
          <SignInPage
            authError={props.authError}
            onDemo={props.onDemo}
            navigate={navigate}
          />
        )}
        {page === "subscribe" && (
          <SubscribePage
            selectedPlan={selectedPlan}
            checkoutBusy={checkoutBusy}
            checkoutStatus={checkoutStatus}
            setSelectedPlan={setSelectedPlan}
            startCheckout={startCheckout}
          />
        )}
      </main>
      <MarketingFooter navigate={navigate} />
    </div>
  );
}

function MarketingHeader(props: {
  page: PublicPage;
  signedIn?: boolean;
  navigate: (page: PublicPage, planId?: SubscriptionPlanId) => void;
  onOpenPortal: () => void;
}) {
  const link = (event: MouseEvent<HTMLAnchorElement>, page: PublicPage) => {
    event.preventDefault();
    props.navigate(page);
  };

  return (
    <header className="marketingHeader">
      <div className="marketingNav pageContainer">
        <a className="brandLockup" href="/" onClick={(event) => link(event, "home")} aria-label="The Script home">
          <span className="brandMark" aria-hidden="true"><Sparkles size={19} /></span>
          <span><strong>The Script</strong><small>Words that move</small></span>
        </a>
        <nav className="marketingLinks" aria-label="Main navigation">
          <a href="/#how-it-works">How it works</a>
          <a href="/#for-educators">For educators</a>
          <a
            href="/pricing"
            aria-current={props.page === "pricing" ? "page" : undefined}
            onClick={(event) => link(event, "pricing")}
          >
            Plans
          </a>
        </nav>
        <div className="marketingActions">
          {props.signedIn ? (
            <button className="ghostButton" onClick={props.onOpenPortal}>Dashboard</button>
          ) : (
            <button className="ghostButton" onClick={() => props.navigate("login")}>Log in</button>
          )}
          <button className="marketingPrimaryButton" onClick={() => props.navigate("subscribe", "individual-plus")}>
            Start learning <ArrowRight size={16} />
          </button>
        </div>
      </div>
    </header>
  );
}

function HomePage(props: {
  signedIn?: boolean;
  navigate: (page: PublicPage, planId?: SubscriptionPlanId) => void;
  onOpenPortal: () => void;
}) {
  return (
    <>
      <section className="marketingHero">
        <div className="pageContainer heroGrid">
          <div className="heroCopy">
            <div className="announcementPill"><Sparkles size={15} /> Grammar becomes an adventure</div>
            <h1>Help children use English, not just memorise it.</h1>
            <p className="heroLead">
              A language-learning RPG where words shape the world—and every speaking, writing, and grammar attempt becomes useful progress for adults.
            </p>
            <div className="heroActions">
              <button className="marketingPrimaryButton largeAction" onClick={() => props.navigate("subscribe", "individual-plus")}>
                Explore individual learning <ArrowRight size={18} />
              </button>
              <button className="marketingSecondaryButton largeAction" onClick={props.signedIn ? props.onOpenPortal : () => props.navigate("login")}>
                <Play size={18} /> {props.signedIn ? "Open dashboard" : "Try the teacher demo"}
              </button>
            </div>
            <ul className="heroReassurance" aria-label="Product highlights">
              <li><Check size={16} /> Context-rich grammar practice</li>
              <li><Check size={16} /> Child-safe learning support</li>
              <li><Check size={16} /> Clear adult insights</li>
            </ul>
          </div>
          <ProductPreview />
        </div>
      </section>

      <section className="trustStrip" aria-label="Designed for the whole learning circle">
        <div className="pageContainer trustStripInner">
          <span>One connected learning circle</span>
          <strong><Gamepad2 size={20} /> Children play</strong>
          <strong><GraduationCap size={20} /> Teachers guide</strong>
          <strong><HeartHandshake size={20} /> Families understand</strong>
        </div>
      </section>

      <section className="marketingSection pageContainer" id="how-it-works">
        <SectionIntro
          eyebrow="Learning that transfers"
          title="Every quest practises language with a purpose."
          copy="Children hear language in context, respond, and use the same grammar to solve problems in the world."
        />
        <div className="featureGrid">
          <FeatureCard
            icon={<BookOpenCheck />}
            number="01"
            title="Meet language in context"
            copy="Town and route conversations make the current grammar idea feel useful and memorable."
            tone="violet"
          />
          <FeatureCard
            icon={<Target />}
            number="02"
            title="Use words to act"
            copy="Speaking, sentence building, and combat turn vocabulary into choices with visible consequences."
            tone="coral"
          />
          <FeatureCard
            icon={<BarChart3 />}
            number="03"
            title="See the next best step"
            copy="Adults get understandable evidence, class focus, and recommendations—not a wall of raw scores."
            tone="mint"
          />
        </div>
      </section>

      <section className="educatorBand" id="for-educators">
        <div className="pageContainer educatorGrid">
          <div className="educatorMock" aria-label="Teacher planning preview">
            <div className="mockTopline"><span>Learning plan</span><span className="statusDot">Ready for today</span></div>
            <div className="focusCard">
              <span className="miniEyebrow">Current destination</span>
              <strong>Adjective Grove</strong>
              <p>Describe a noun clearly using size and colour.</p>
              <div className="mockTokens"><span>big</span><span>small</span><span>bright</span><span>noun</span></div>
            </div>
            <div className="mockStudentRows">
              <MockStudent name="Meera" detail="Ready for an independent challenge" progress={88} />
              <MockStudent name="Kabir" detail="Practise adjective order" progress={64} />
              <MockStudent name="Zoya" detail="Needs one guided example" progress={42} />
            </div>
          </div>
          <div className="educatorCopy">
            <p className="sectionEyebrow">A calmer teacher workspace</p>
            <h2>Know what happened—and what to do tomorrow.</h2>
            <p>Plan a shared class goal, adjust practice for one learner, and review speech or handwriting evidence without hunting through menus.</p>
            <ul className="checkList">
              <li><span><Check size={16} /></span><div><strong>Start with priorities</strong><p>Completion, confidence, and review flags are visible at a glance.</p></div></li>
              <li><span><Check size={16} /></span><div><strong>Keep differentiation practical</strong><p>Give one learner a custom duration, vocabulary set, or revision focus.</p></div></li>
              <li><span><Check size={16} /></span><div><strong>Keep evidence understandable</strong><p>Connect attempts to grammar, vocabulary, pronunciation, and support used.</p></div></li>
            </ul>
            <button className="marketingSecondaryButton" onClick={() => props.navigate("login")}>
              Open the teacher demo <ChevronRight size={17} />
            </button>
          </div>
        </div>
      </section>

      <section className="safetySection pageContainer">
        <div>
          <p className="sectionEyebrow">Trust is part of the product</p>
          <h2>Built around children, with adults in control.</h2>
        </div>
        <div className="safetyGrid">
          <article><ShieldCheck /><strong>Role-scoped access</strong><p>Teachers, administrators, families, and learners see only the views intended for them.</p></article>
          <article><LockKeyhole /><strong>Consent-aware features</strong><p>Buddy, audio, handwriting evidence, and diagnostics can be governed separately.</p></article>
          <article><ClipboardList /><strong>Useful audit trails</strong><p>Important setup, privacy, and learning-plan changes have a clear operational path.</p></article>
        </div>
      </section>

      <section className="closingCta">
        <div className="pageContainer closingCtaInner">
          <div><p className="sectionEyebrow">Ready for the next chapter?</p><h2>Give every new word somewhere to go.</h2></div>
          <div className="heroActions">
            <button className="marketingPrimaryButton lightButton" onClick={() => props.navigate("subscribe", "individual-plus")}>View plans <ArrowRight size={17} /></button>
            <button className="marketingSecondaryButton darkOutline" onClick={() => props.navigate("login")}>Log in</button>
          </div>
        </div>
      </section>
    </>
  );
}

function ProductPreview() {
  return (
    <div className="heroVisual" aria-label="Product dashboard preview">
      <div className="heroOrb orbOne" />
      <div className="heroOrb orbTwo" />
      <div className="dashboardPreview">
        <div className="previewSidebar">
          <span className="previewLogo"><Sparkles size={16} /></span>
          {[0, 1, 2, 3].map((item) => <span className={item === 0 ? "previewNav active" : "previewNav"} key={item} />)}
        </div>
        <div className="previewContent">
          <div className="previewHeader"><div><small>Good morning, Ms Rao</small><strong>Classroom pulse</strong></div><span className="previewAvatar">MR</span></div>
          <div className="previewMetrics">
            <div><span>18</span><small>practised</small></div>
            <div><span>82%</span><small>confidence</small></div>
            <div><span>4</span><small>need review</small></div>
          </div>
          <div className="previewPanel">
            <div className="previewPanelTitle"><div><strong>Today’s focus</strong><small>Nounfield Town</small></div><span>72%</span></div>
            <div className="progressTrack"><span style={{ width: "72%" }} /></div>
            <div className="previewWordRow"><span>a rat</span><span>the cat</span><span>noun</span></div>
          </div>
          <div className="previewBottomGrid">
            <div className="previewPanel compact"><strong>Class growth</strong><div className="miniBars">{[35, 54, 42, 68, 62, 85].map((height, index) => <span style={{ height: `${height}%` }} key={index} />)}</div></div>
            <div className="previewPanel compact"><strong>Next action</strong><p>Review article + noun with 4 learners.</p><button tabIndex={-1}>Open group</button></div>
          </div>
        </div>
      </div>
      <div className="floatingInsight"><span><Target size={17} /></span><div><strong>Grammar goal cleared</strong><small>6 learners today</small></div></div>
    </div>
  );
}

function PricingPage(props: {
  checkoutBusy: SubscriptionPlanId | null;
  checkoutStatus: string;
  navigate: (page: PublicPage, planId?: SubscriptionPlanId) => void;
  startCheckout: (planId: SubscriptionPlanId) => void;
}) {
  return (
    <section className="pricingPage pageContainer">
      <SectionIntro
        eyebrow="Simple launch plans"
        title="Choose a home tier or institution route."
        copy="Families can pick the individual tier that fits their child. Teachers and school teams use one School / Educator route for classroom access."
      />
      <PlanGrid
        busy={props.checkoutBusy}
        onChoose={(planId) => {
          props.navigate("subscribe", planId);
          if (subscriptionProvider.isPlanConfigured(planId)) void props.startCheckout(planId);
        }}
      />
      <p className="pricingNote">
        Displayed prices are configurable launch labels; the connected checkout provider is the billing source of truth. Taxes and regional terms may apply.
      </p>
      <CheckoutNotice message={props.checkoutStatus} />
      <div className="faqGrid">
        <article><h3>Can a family subscribe directly?</h3><p>Yes. Individual tiers are designed for home access, from one child to sibling profiles.</p></article>
        <article><h3>Can I start without payment?</h3><p>A safe demo workspace is available from the login page. A live deployment decides trial and checkout terms in its billing provider.</p></article>
        <article><h3>What happens if checkout is not connected?</h3><p>The site explains that billing is pending and does not create a false subscription or charge.</p></article>
        <article><h3>Where do teachers subscribe?</h3><p>Use the School / Educator route. It covers teacher workspaces, classroom access, and school rollout conversations.</p></article>
      </div>
    </section>
  );
}

function SubscribePage(props: {
  selectedPlan: SubscriptionPlanId;
  checkoutBusy: SubscriptionPlanId | null;
  checkoutStatus: string;
  setSelectedPlan: (planId: SubscriptionPlanId) => void;
  startCheckout: (planId: SubscriptionPlanId) => void;
}) {
  const plan = useMemo(
    () => subscriptionPlans.find((item) => item.id === props.selectedPlan) ?? subscriptionPlans[0],
    [props.selectedPlan]
  );

  return (
    <section className="subscribePage pageContainer">
      <div className="subscribeIntro">
        <p className="sectionEyebrow">Choose your plan</p>
        <h1>Pick a home tier or school access.</h1>
        <p>You’ll continue only if this deployment has a verified hosted checkout destination.</p>
      </div>
      <div className="subscribeGrid">
        <div className="planSelector" role="radiogroup" aria-label="Subscription plan">
          {subscriptionPlans.map((item) => (
            <button
              key={item.id}
              className={item.id === props.selectedPlan ? "planChoice active" : "planChoice"}
              role="radio"
              aria-checked={item.id === props.selectedPlan}
              onClick={() => props.setSelectedPlan(item.id)}
            >
              <span><strong>{item.name}</strong><small>{item.audience}</small></span>
              <span className="planChoicePrice">{item.price}<small>{item.cadence}</small></span>
            </button>
          ))}
        </div>
        <aside className="checkoutSummary">
          <span className="summaryIcon"><UserRound /></span>
          <p className="sectionEyebrow">Your selection</p>
          <h2>{plan.name}</h2>
          <p>{plan.description}</p>
          <ul>{plan.features.map((feature) => <li key={feature}><Check size={16} /> {feature}</li>)}</ul>
          <div className="summaryPrice"><strong>{plan.price}</strong><span>{plan.cadence}</span></div>
          <button
            className="marketingPrimaryButton checkoutButton"
            disabled={props.checkoutBusy !== null}
            onClick={() => void props.startCheckout(plan.id)}
          >
            {props.checkoutBusy === plan.id ? <RefreshCw className="spin" size={17} /> : <LockKeyhole size={17} />}
            {props.checkoutBusy === plan.id ? "Opening secure checkout…" : plan.actionLabel}
          </button>
          <p className="checkoutFinePrint">
            {subscriptionProvider.isPlanConfigured(plan.id)
              ? "Checkout and final price are provided by the configured billing service."
              : "Checkout connection pending. Clicking continue will not charge you."}
          </p>
          <CheckoutNotice message={props.checkoutStatus} />
        </aside>
      </div>
    </section>
  );
}

function SignInPage(props: {
  authError?: string;
  onDemo: (role: UserRole) => void;
  navigate: (page: PublicPage, planId?: SubscriptionPlanId) => void;
}) {
  const [email, setEmail] = useState(portalAuthProvider.isConfigured ? "" : "teacher@littlelantern.school");
  const [password, setPassword] = useState("");
  const [error, setError] = useState(props.authError ?? "");
  const [info, setInfo] = useState("");
  const [busy, setBusy] = useState(false);

  useEffect(() => setError(props.authError ?? ""), [props.authError]);

  const login = async (event: FormEvent) => {
    event.preventDefault();
    if (!portalAuthProvider.isConfigured) {
      props.onDemo("teacher");
      return;
    }
    if (!email.trim() || !password) {
      setError("Enter your email and password.");
      return;
    }
    setBusy(true);
    setError("");
    setInfo("Signing you in…");
    try {
      await portalAuthProvider.signIn(email, password);
      setInfo("Signed in. Opening your workspace…");
    } catch (err) {
      setInfo("");
      setError(portalAuthProvider.describeError(err));
    } finally {
      setBusy(false);
    }
  };

  const resetPassword = async () => {
    if (!email.trim()) {
      setError("Enter your account email first.");
      return;
    }
    if (!portalAuthProvider.isConfigured) {
      setInfo("Password reset is unavailable in the local demo. Choose a demo workspace below.");
      return;
    }
    setBusy(true);
    setError("");
    setInfo("");
    try {
      await portalAuthProvider.sendPasswordReset(email);
      setInfo("Password reset email sent. Check your inbox.");
    } catch (err) {
      setError(portalAuthProvider.describeError(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="signInPage pageContainer">
      <div className="signInStory">
        <span className="authIllustration"><BookOpenCheck size={34} /></span>
        <p className="sectionEyebrow">Welcome back</p>
        <h1>Pick up where your learners left off.</h1>
        <p>Open the right workspace automatically—teacher, school administrator, learner, or family view.</p>
        <div className="authBenefit"><ShieldCheck /><div><strong>Role-aware by design</strong><span>Your account profile controls which school and learner records are loaded.</span></div></div>
        <div className="authBenefit"><BarChart3 /><div><strong>Today before everything</strong><span>See the current class goal, progress, and next teaching action first.</span></div></div>
      </div>
      <div className="authCard">
        <div><p className="sectionEyebrow">Portal login</p><h2>Sign in to The Script</h2><p>Use the email connected to your school or family account.</p></div>
        <form className="authForm" onSubmit={login}>
          <label>Email address<input type="email" autoComplete="email" value={email} onChange={(event) => setEmail(event.target.value)} /></label>
          <label>Password<input type="password" autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} /></label>
          <div className="authRow"><span className="authSecure"><LockKeyhole size={14} /> Secure sign in</span><button type="button" className="textButton" disabled={busy} onClick={() => void resetPassword()}>Forgot password?</button></div>
          <button className="marketingPrimaryButton authSubmit" disabled={busy}>
            {busy ? <RefreshCw className="spin" size={17} /> : <ArrowRight size={17} />}
            {busy ? "Signing in…" : portalAuthProvider.isConfigured ? "Sign in" : "Open teacher demo"}
          </button>
        </form>
        <div className="authMessage" aria-live="polite">
          {error && <p className="authError">{error}</p>}
          {info && <p className="authInfo">{info}</p>}
        </div>
        <div className="authDivider"><span>or explore safely</span></div>
        <div className="demoRoleGrid">
          <button onClick={() => props.onDemo("teacher")}><GraduationCap size={17} /> Teacher demo</button>
          <button onClick={() => props.onDemo("admin")}><Users size={17} /> Admin demo</button>
          <button onClick={() => props.onDemo("student")}><Gamepad2 size={17} /> Learner demo</button>
        </div>
        <p className="authFootnote">Need a new individual account? <button className="textButton" onClick={() => props.navigate("subscribe", "individual-plus")}>View individual tiers</button></p>
      </div>
    </section>
  );
}

function PlanGrid(props: { busy: SubscriptionPlanId | null; onChoose: (planId: SubscriptionPlanId) => void }) {
  return (
    <div className="planGrid">
      {subscriptionPlans.map((plan) => (
        <article className={plan.featured ? "planCard featured" : "planCard"} key={plan.id}>
          {plan.featured && <span className="popularPill">Most useful for teachers</span>}
          <p className="planAudience">{plan.audience}</p>
          <h2>{plan.name}</h2>
          <div className="planPrice"><strong>{plan.price}</strong><span>{plan.cadence}</span></div>
          <p>{plan.description}</p>
          <ul>{plan.features.map((feature) => <li key={feature}><Check size={16} /> {feature}</li>)}</ul>
          <button className={plan.featured ? "marketingPrimaryButton" : "marketingSecondaryButton"} disabled={props.busy !== null} onClick={() => props.onChoose(plan.id)}>
            {props.busy === plan.id ? <RefreshCw className="spin" size={16} /> : null}{plan.actionLabel}
          </button>
        </article>
      ))}
    </div>
  );
}

function FeatureCard(props: { icon: ReactNode; number: string; title: string; copy: string; tone: string }) {
  return <article className={`featureCard ${props.tone}`}><div className="featureIcon">{props.icon}</div><span>{props.number}</span><h3>{props.title}</h3><p>{props.copy}</p></article>;
}

function SectionIntro(props: { eyebrow: string; title: string; copy: string }) {
  return <div className="sectionIntro"><p className="sectionEyebrow">{props.eyebrow}</p><h2>{props.title}</h2><p>{props.copy}</p></div>;
}

function MockStudent(props: { name: string; detail: string; progress: number }) {
  return <div className="mockStudent"><span className="mockAvatar">{props.name.slice(0, 1)}</span><div><strong>{props.name}</strong><small>{props.detail}</small><span className="progressTrack"><span style={{ width: `${props.progress}%` }} /></span></div><em>{props.progress}%</em></div>;
}

function MarketingFooter(props: { navigate: (page: PublicPage, planId?: SubscriptionPlanId) => void }) {
  return (
    <footer className="marketingFooter">
      <div className="pageContainer footerGrid">
        <div className="brandLockup footerBrand"><span className="brandMark"><Sparkles size={19} /></span><span><strong>The Script</strong><small>Words that move</small></span></div>
        <div><strong>Product</strong><button onClick={() => props.navigate("home")}>How it works</button><button onClick={() => props.navigate("pricing")}>Plans</button><button onClick={() => props.navigate("login")}>Portal login</button></div>
        <div><strong>For adults</strong><button onClick={() => props.navigate("subscribe", "individual-plus")}>Families</button><button onClick={() => props.navigate("subscribe", "institution")}>School / Educator</button></div>
        <div><strong>Safety</strong><a href="/privacy.html">Privacy</a><a href="/child-safety.html">Child safety</a><span><Mail size={14} /> Support details in your school rollout</span></div>
      </div>
      <div className="pageContainer footerBottom"><span>© {new Date().getFullYear()} The Script</span><span>Learning should feel alive.</span></div>
    </footer>
  );
}

function pageFromLocation(): PublicPage {
  const path = window.location.pathname.toLowerCase().replace(/\/$/, "") || "/";
  if (path === "/pricing") return "pricing";
  if (path === "/subscribe" || path === "/signup") return "subscribe";
  if (path === "/login" || path === "/portal") return "login";
  return "home";
}

function planFromLocation(): SubscriptionPlanId {
  const plan = new URLSearchParams(window.location.search).get("plan");
  if (plan === "institution" || plan === "educator" || plan === "school") return "institution";
  if (plan === "individual-starter" || plan === "starter") return "individual-starter";
  if (plan === "individual-family" || plan === "family") return "individual-family";
  return "individual-plus";
}

function pathForPage(page: PublicPage, planId?: SubscriptionPlanId): string {
  if (page === "home") return "/";
  if (page === "pricing") return "/pricing";
  if (page === "login") return "/login";
  return `/subscribe${planId ? `?plan=${planId}` : ""}`;
}

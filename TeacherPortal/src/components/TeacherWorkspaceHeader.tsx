import { BarChart3, CalendarDays, CheckCircle2, ClipboardCheck, GraduationCap } from "lucide-react";

interface TeacherWorkspaceHeaderProps {
  teacherName: string;
  className: string;
  completedLearners: number;
  learnerCount: number;
  practiceDuration: string;
  priorityReviews: number;
  destination: string;
  onPlanLearning: () => void;
  onReviewLearners: () => void;
}

export default function TeacherWorkspaceHeader(props: TeacherWorkspaceHeaderProps) {
  const completionPercent = props.learnerCount > 0
    ? Math.round((props.completedLearners / props.learnerCount) * 100)
    : 0;
  const practiceMessage = props.completedLearners === props.learnerCount && props.learnerCount > 0
    ? "Today's class practice is complete. Review the evidence or prepare tomorrow's focus."
    : `${props.completedLearners} of ${props.learnerCount} learners have completed today's ${props.practiceDuration} practice.`;

  return (
    <section className="teacherHero">
      <div className="teacherHeroCopy">
        <p className="teacherKicker">Teacher workspace · {props.className}</p>
        <h1>Welcome back, {firstName(props.teacherName)}.</h1>
        <p>{practiceMessage}</p>
        <div className="teacherHeroActions">
          <button className="primaryButton fitButton" onClick={props.onPlanLearning}><CalendarDays size={17} /> Plan learning</button>
          <button className="secondaryButton" onClick={props.onReviewLearners}><BarChart3 size={17} /> Review learners</button>
        </div>
      </div>
      <div className="teacherHeroSummary">
        <div className="heroSummaryTop"><span>Today's pulse</span><strong>{completionPercent}% complete</strong></div>
        <div className="heroProgress"><span style={{ width: `${completionPercent}%` }} /></div>
        <div className="heroSummaryGrid">
          <div><span className="summaryBadge success"><CheckCircle2 size={16} /></span><strong>{props.completedLearners}</strong><small>practised</small></div>
          <div><span className="summaryBadge focus"><ClipboardCheck size={16} /></span><strong>{props.priorityReviews}</strong><small>priority reviews</small></div>
          <div><span className="summaryBadge goal"><GraduationCap size={16} /></span><strong>{props.destination}</strong><small>class destination</small></div>
        </div>
      </div>
    </section>
  );
}

function firstName(displayName: string) {
  return displayName.trim().split(/\s+/)[0] || "there";
}

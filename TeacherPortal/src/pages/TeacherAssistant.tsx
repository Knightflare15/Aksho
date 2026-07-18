import { Bot, FileSearch, Send, Sparkles } from "lucide-react";
import { useMemo, useState } from "react";
import {
  askTeacherAssistant,
  type PortalDataset,
  type TeacherAssistantResponse
} from "../portalData";
import type { UserProfile } from "../types";

interface ChatTurn {
  id: string;
  question: string;
  response?: TeacherAssistantResponse;
  status: "loading" | "done" | "error";
  error?: string;
}

const promptStarters = [
  "Which learners need the most support this week?",
  "Create a 10-minute remediation plan for the selected learner.",
  "What evidence explains the latest high-priority recommendations?",
  "Which issues are grammar vs pronunciation vs handwriting?"
];

export function TeacherAssistant(props: {
  profile: UserProfile;
  dataset: PortalDataset;
}) {
  const [selectedStudentId, setSelectedStudentId] = useState("");
  const [question, setQuestion] = useState(promptStarters[0]);
  const [turns, setTurns] = useState<ChatTurn[]>([]);
  const [isAsking, setIsAsking] = useState(false);

  const selectedStudent = useMemo(
    () => props.dataset.students.find((student) => student.id === selectedStudentId),
    [props.dataset.students, selectedStudentId]
  );
  const scopeLabel = selectedStudent ? selectedStudent.name : props.dataset.activeClass.name;

  async function submit(nextQuestion = question) {
    const cleanQuestion = nextQuestion.trim();
    if (!cleanQuestion || isAsking) return;
    const turnId = `${Date.now()}-${Math.random().toString(36).slice(2)}`;
    setIsAsking(true);
    setQuestion(cleanQuestion);
    setTurns((current) => [
      {
        id: turnId,
        question: cleanQuestion,
        status: "loading"
      },
      ...current
    ]);
    try {
      const response = await askTeacherAssistant({
        schoolId: props.profile.schoolId,
        classId: props.dataset.activeClass.id,
        studentId: selectedStudentId || undefined,
        question: cleanQuestion
      }, props.dataset);
      setTurns((current) => current.map((turn) => (
        turn.id === turnId ? { ...turn, response, status: "done" } : turn
      )));
    } catch (error) {
      const message = error instanceof Error ? error.message : "Teacher Assistant could not answer right now.";
      setTurns((current) => current.map((turn) => (
        turn.id === turnId ? { ...turn, status: "error", error: message } : turn
      )));
    } finally {
      setIsAsking(false);
    }
  }

  return (
    <div className="pageStack">
      <section className="panel teacherAssistantShell">
        <div className="teacherAssistantHeader">
          <div>
            <span className="eyebrow"><Sparkles size={14} /> Teacher Insight Assistant</span>
            <h2>Ask about class patterns, learner evidence, and next actions</h2>
            <p>Answers are grounded in recent portal records and return source IDs for review.</p>
          </div>
          <div className="assistantScope">
            <label>
              Scope
              <select value={selectedStudentId} onChange={(event) => setSelectedStudentId(event.target.value)}>
                <option value="">Class: {props.dataset.activeClass.name}</option>
                {props.dataset.students.map((student) => (
                  <option key={student.id} value={student.id}>{student.name}</option>
                ))}
              </select>
            </label>
          </div>
        </div>

        <div className="assistantPromptGrid">
          {promptStarters.map((starter) => (
            <button
              className="secondaryButton"
              key={starter}
              onClick={() => {
                setQuestion(starter);
                void submit(starter);
              }}
              disabled={isAsking}
            >
              {starter}
            </button>
          ))}
        </div>

        <div className="assistantComposer">
          <FileSearch size={18} />
          <textarea
            value={question}
            onChange={(event) => setQuestion(event.target.value)}
            rows={3}
            placeholder={`Ask about ${scopeLabel}`}
          />
          <button className="primaryButton" onClick={() => void submit()} disabled={isAsking || !question.trim()}>
            <Send size={16} />
            Ask
          </button>
        </div>
      </section>

      <section className="assistantConversation">
        {turns.length === 0 && (
          <article className="panel assistantEmpty">
            <Bot size={24} />
            <div>
              <h2>No assistant runs yet</h2>
              <p>Try a class-level question first, then narrow to a learner when you need evidence for a meeting or mission override.</p>
            </div>
          </article>
        )}
        {turns.map((turn) => (
          <article className="panel assistantTurn" key={turn.id} data-state={turn.status}>
            <div className="assistantQuestion">
              <span>Teacher</span>
              <strong>{turn.question}</strong>
            </div>
            {turn.status === "loading" && <p className="muted">Retrieving evidence and asking the analyst...</p>}
            {turn.status === "error" && <p className="warningText">{turn.error}</p>}
            {turn.response && (
              <div className="assistantAnswer">
                <p>{turn.response.answer}</p>
                <div className="assistantColumns">
                  <div>
                    <h3>Suggested Actions</h3>
                    <ul>
                      {turn.response.suggestedActions.map((action) => <li key={action}>{action}</li>)}
                    </ul>
                  </div>
                  <div>
                    <h3>Evidence Sources</h3>
                    <ul>
                      {turn.response.citations.map((citation) => <li key={citation}><code>{citation}</code></li>)}
                    </ul>
                  </div>
                </div>
                <details className="assistantTrace">
                  <summary>Agent Trace · {turn.response.model}</summary>
                  <ol>
                    {turn.response.agentTrace.map((step) => <li key={step}>{step}</li>)}
                  </ol>
                  {turn.response.fallbackReason && <p>Fallback: {turn.response.fallbackReason}</p>}
                </details>
              </div>
            )}
          </article>
        ))}
      </section>
    </div>
  );
}

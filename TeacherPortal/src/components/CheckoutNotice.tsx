import { useEffect, useRef } from "react";

export default function CheckoutNotice(props: { message: string }) {
  const noticeRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!props.message) {
      return;
    }

    const frameId = window.requestAnimationFrame(() => noticeRef.current?.focus());
    return () => window.cancelAnimationFrame(frameId);
  }, [props.message]);

  if (!props.message) {
    return null;
  }

  return (
    <div
      ref={noticeRef}
      className="checkoutNotice"
      role="status"
      aria-live="polite"
      tabIndex={-1}
    >
      {props.message}
    </div>
  );
}

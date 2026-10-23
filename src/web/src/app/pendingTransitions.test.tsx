import { render, screen } from "@testing-library/react";
import { startTransition, useEffect, useState } from "react";
import { describe, expect, it } from "vitest";

import { useAfterPendingTransitions } from "./pendingTransitions";

interface Controls {
  afterPendingTransitions: () => Promise<void>;
  show: (text: string) => void;
}

/** Renders a line of text and hands back the hook, plus a way to change the text. */
function renderText() {
  let controls: Controls | undefined;
  function Text() {
    const [text, setText] = useState("Before");
    const afterPendingTransitions = useAfterPendingTransitions();
    useEffect(() => {
      controls = { afterPendingTransitions, show: setText };
    });
    return <p>{text}</p>;
  }

  const view = render(<Text />);
  return { ...view, controls: controls! };
}

/** Whether the promise settles within a moment, without failing by timeout. */
function settlesSoon(promise: Promise<void>): Promise<boolean> {
  return Promise.race([
    promise.then(() => true),
    new Promise<boolean>((resolve) => setTimeout(() => resolve(false), 200)),
  ]);
}

describe("useAfterPendingTransitions", () => {
  it("resolves once a transition started earlier has rendered", async () => {
    const { controls } = renderText();

    startTransition(() => controls.show("After"));
    await controls.afterPendingTransitions();

    expect(screen.getByText("After")).toBeInTheDocument();
  });

  it("settles a wait when the component unmounts before rendering it", async () => {
    const { controls, unmount } = renderText();

    const wait = controls.afterPendingTransitions();
    unmount();

    expect(await settlesSoon(wait)).toBe(true);
  });

  it("settles at once when asked after the component unmounted", async () => {
    const { controls, unmount } = renderText();

    unmount();

    expect(await settlesSoon(controls.afterPendingTransitions())).toBe(true);
  });
});

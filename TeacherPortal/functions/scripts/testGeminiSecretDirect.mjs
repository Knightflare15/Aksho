#!/usr/bin/env node

let input = "";
process.stdin.setEncoding("utf8");
process.stdin.on("data", chunk => {
  input += chunk;
});

process.stdin.on("end", async () => {
  const key = input.trim().replace(/^['"]|['"]$/g, "").trim();
  if (!key) {
    console.log("NO_KEY_ON_STDIN");
    process.exitCode = 1;
    return;
  }

  const generateContentBody = {
    contents: [
      {
        role: "user",
        parts: [{ text: "Reply with exactly: OK" }]
      }
    ]
  };

  const generateContentEndpoint =
    "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash:generateContent" +
    `?key=${encodeURIComponent(key)}`;

  try {
    const response = await fetch(generateContentEndpoint, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "x-goog-api-key": key
      },
      body: JSON.stringify(generateContentBody)
    });

    const raw = await response.text();
    console.log(`generateContent.status=${response.status}`);
    if (!response.ok) {
      console.log(raw.slice(0, 700));
    } else {
      const payload = JSON.parse(raw);
      const text = (payload.candidates?.[0]?.content?.parts ?? [])
        .map(part => part.text ?? "")
        .join(" ")
        .trim();
      console.log(`generateContent.text=${text.slice(0, 200)}`);
    }

    const interactionsBody = {
      model: "gemini-3.5-flash",
      input: "Reply with exactly: OK",
      store: false
    };
    const interactionsResponse = await fetch("https://generativelanguage.googleapis.com/v1beta/interactions", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "x-goog-api-key": key
      },
      body: JSON.stringify(interactionsBody)
    });
    const interactionsRaw = await interactionsResponse.text();
    console.log(`interactions.status=${interactionsResponse.status}`);
    if (!interactionsResponse.ok) {
      console.log(interactionsRaw.slice(0, 700));
      process.exitCode = 1;
      return;
    }

    const interaction = JSON.parse(interactionsRaw);
    const text = extractInteractionText(interaction);
    console.log(`interactions.text=${text.slice(0, 200)}`);
  } catch (error) {
    console.log(`ERROR ${error instanceof Error ? error.message : String(error)}`);
    process.exitCode = 1;
  }
});

function extractInteractionText(value) {
  const found = [];
  walk(value, found);
  return found.join(" ").trim();
}

function walk(value, found) {
  if (!value || found.length > 8) return;
  if (typeof value === "string") return;
  if (Array.isArray(value)) {
    for (const entry of value) walk(entry, found);
    return;
  }
  if (typeof value !== "object") return;
  if (typeof value.text === "string") found.push(value.text);
  for (const child of Object.values(value)) walk(child, found);
}

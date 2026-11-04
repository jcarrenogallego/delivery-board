(() => {
  const form = document.querySelector("#assistant-form");
  const question = document.querySelector("#assistant-question");
  const provider = document.querySelector("#assistant-provider");
  const submit = document.querySelector("#assistant-submit");
  const status = document.querySelector("#assistant-status");
  const result = document.querySelector("#assistant-result");
  const answer = document.querySelector("#assistant-answer");
  let pending = false;

  const errorMessage = code => {
    if (code === 400) return "Revisa la pregunta y el proveedor seleccionado.";
    if (code === 503) return "El asistente o el proveedor no está disponible. Inténtalo más tarde.";
    if (code === 504) return "La consulta ha tardado demasiado. Inténtalo de nuevo.";
    return "No se pudo obtener la respuesta. Inténtalo de nuevo.";
  };

  form.addEventListener("submit", async event => {
    event.preventDefault();
    if (pending) return;
    const text = question.value.trim();
    if (!text) {
      status.textContent = "Escribe una pregunta para consultar el tablero.";
      status.className = "assistant-status assistant-error";
      question.focus();
      return;
    }

    const selectedProvider = provider.value;
    pending = true;
    submit.disabled = true;
    provider.disabled = true;
    form.setAttribute("aria-busy", "true");
    submit.textContent = "Consultando…";
    status.className = "assistant-status";
    status.textContent = "Consultando…";
    answer.textContent = "";
    result.hidden = true;

    let failureMessage = errorMessage();
    try {
      const response = await fetch("/api/assistant/query", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ provider: selectedProvider, question: text })
      });
      if (!response.ok) {
        failureMessage = errorMessage(response.status);
        throw new Error();
      }
      const body = await response.json();
      if (!body || typeof body.answer !== "string" || !body.answer.trim() ||
          body.provider !== selectedProvider || typeof body.model !== "string" || !body.model.trim()) {
        throw new Error();
      }
      answer.textContent = body.answer;
      result.hidden = false;
      status.textContent = "Respuesta recibida.";
    } catch {
      status.textContent = failureMessage;
      status.className = "assistant-status assistant-error";
    } finally {
      pending = false;
      submit.disabled = false;
      provider.disabled = false;
      form.setAttribute("aria-busy", "false");
      submit.textContent = "Consultar";
    }
  });
})();

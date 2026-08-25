import "@fontsource-variable/jetbrains-mono";
import "@fontsource-variable/manrope";
import "./style.css";

const copyButtons = document.querySelectorAll<HTMLButtonElement>(
  "button[data-copy-target]",
);

for (const button of copyButtons) {
  const targetId = button.dataset.copyTarget;
  const target = targetId ? document.getElementById(targetId) : null;
  const label = button.querySelector<HTMLElement>("[data-copy-label]");

  if (!target || !label) {
    continue;
  }

  button.addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(target.textContent ?? "");
      label.textContent = "Copied";
    } catch {
      const selection = window.getSelection();
      const range = document.createRange();
      range.selectNodeContents(target);
      selection?.removeAllRanges();
      selection?.addRange(range);
      label.textContent = "Select and copy";
    }
  });
}

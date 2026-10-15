import type { Task } from "./model.ts";

export function renderList(list: HTMLUListElement, tasks: readonly Task[]): void {
  list.replaceChildren(
    ...tasks.map((task) => {
      const item = document.createElement("li");
      item.textContent = `${task.title} (${task.priority})`;
      if (task.done) item.className = "done";
      return item;
    }),
  );
}

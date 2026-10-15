import { byPriority, summary, type Task } from "./model.ts";
import { renderList } from "./render.ts";

const tasks = [
  { title: "Write the docs", priority: "medium", done: false },
  { title: "Ship the release", priority: "high", done: false },
  { title: "Tidy the samples", priority: "low", done: true },
] satisfies Task[];

const list = document.querySelector("#tasks") as HTMLUListElement;
const status = document.querySelector<HTMLParagraphElement>("#status")!;
const add = document.querySelector("#add") as HTMLButtonElement;

let added: number = 0;

function show(): void {
  renderList(list, byPriority<Task>(tasks));
  status.textContent = summary(tasks);
}

add.addEventListener("click", () => {
  tasks.push({ title: `New task ${++added}`, priority: "low", done: false });
  show();
});

show();

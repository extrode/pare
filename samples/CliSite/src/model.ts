export type Priority = "high" | "medium" | "low";

export interface Task {
  title: string;
  priority: Priority;
  done: boolean;
}

const rank: Record<Priority, number> = { high: 0, medium: 1, low: 2 };

export function byPriority<T extends { priority: Priority }>(items: readonly T[]): T[] {
  return [...items].sort((a, b) => rank[a.priority] - rank[b.priority]);
}

export function summary(tasks: readonly Task[]): string {
  const open = tasks.filter((t) => !t.done).length;
  return `${open} of ${tasks.length} open`;
}

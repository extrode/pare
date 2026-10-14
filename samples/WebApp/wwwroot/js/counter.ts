import { plural, type Plural } from "./format.ts";

export interface Counter {
  count: number;
  increment(): void;
  label(): string;
}

const clicks = { one: "click", other: "clicks" } satisfies Plural;

export function counter(start: number = 0): Counter {
  return {
    count: start,
    increment(this: Counter): void {
      this.count++;
    },
    label(this: Counter): string {
      return plural(this.count, clicks);
    },
  };
}

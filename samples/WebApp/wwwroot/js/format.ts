export type Plural = { one: string; other: string };

export function plural(n: number, forms: Plural): string {
  return `${n} ${n === 1 ? forms.one : forms.other}`;
}

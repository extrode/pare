// The part of Alpine's API this sample uses. A real app vendors the full typings here.
declare module "https://cdn.jsdelivr.net/npm/alpinejs@3.14.8/dist/module.esm.js" {
  interface Alpine {
    data(name: string, factory: (...args: any[]) => object): void;
    start(): void;
  }
  const Alpine: Alpine;
  export default Alpine;
}

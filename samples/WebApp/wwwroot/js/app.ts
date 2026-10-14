import Alpine from "https://cdn.jsdelivr.net/npm/alpinejs@3.14.8/dist/module.esm.js";
import { counter } from "./counter.ts";

Alpine.data("counter", counter);
Alpine.start();

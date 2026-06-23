import { initNetworkSnifferUi } from "./src/ui.js";

initNetworkSnifferUi().catch((error) => {
  console.error("Ice Pick failed to start", error);
});

import { createDevtoolsPanel } from "./src/browserApi.js";

createDevtoolsPanel("Ice Pick", "assets/icon-16.png", "panel.html").catch((error) => {
  console.error("Ice Pick panel creation failed", error);
});

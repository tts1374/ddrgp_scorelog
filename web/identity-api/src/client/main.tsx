import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { PlayerApp } from "./App";
import type { PublicPlayer } from "./types";
import "./styles.css";

function readBootstrap(): PublicPlayer {
  const element = document.getElementById("player-bootstrap");
  if (element === null || element.textContent === null) {
    throw new Error("Player bootstrap data is missing.");
  }
  return JSON.parse(element.textContent) as PublicPlayer;
}

const root = document.getElementById("root");
if (root === null) throw new Error("Application root is missing.");
createRoot(root).render(<StrictMode><PlayerApp player={readBootstrap()} /></StrictMode>);

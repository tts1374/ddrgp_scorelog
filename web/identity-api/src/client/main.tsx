import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { PlayerApp } from "./App";
import { ManagementApp } from "./ManagementApp";
import { HomePage } from "./HomePage";
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
createRoot(root).render(<StrictMode>{location.pathname === "/" ? <HomePage />
  : location.pathname.startsWith("/my/") ? <ManagementApp /> : <PlayerApp player={readBootstrap()} />}</StrictMode>);

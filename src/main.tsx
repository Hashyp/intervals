import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { AuthProvider } from "./auth/AuthProvider";
import { ProtectedApp } from "./auth/ProtectedApp";
import "./styles.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <AuthProvider>
      <ProtectedApp>
        <App />
      </ProtectedApp>
    </AuthProvider>
  </StrictMode>,
);

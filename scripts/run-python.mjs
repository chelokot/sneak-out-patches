import { repositoryRoot, resolvePythonCommand, runCommand } from "./lib/workspace-tools.mjs";

const scriptArguments = process.argv.slice(2);
if (scriptArguments.length === 0) {
  throw new Error("Usage: node scripts/run-python.mjs <script> [args...]");
}

const python = await resolvePythonCommand();
await runCommand(python.command, [...python.prefix, ...scriptArguments], {
  cwd: repositoryRoot
});

import { localDotnetExecutablePath, runCommand } from "./lib/workspace-tools.mjs";

const argumentsList = process.argv.slice(2);
if (argumentsList.length === 0) {
  throw new Error("Usage: node scripts/run-dotnet.mjs <dotnet-args...>");
}

await runCommand(localDotnetExecutablePath(), argumentsList);

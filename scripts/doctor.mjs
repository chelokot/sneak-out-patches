import {
  fileExists,
  localDotnetExecutablePath,
  resolvePythonCommand,
  runAndCapture
} from "./lib/workspace-tools.mjs";

async function printTool(name, executablePath, versionArguments) {
  const exists = await fileExists(executablePath);
  if (!exists) {
    console.log(`${name}: missing (${executablePath})`);
    return;
  }

  const { stdout } = await runAndCapture(executablePath, versionArguments);
  console.log(`${name}: ${stdout.trim()}`);
}

const python = await resolvePythonCommand();
const pythonVersion = await runAndCapture(python.command, [...python.prefix, "--version"]);
console.log(`python: ${pythonVersion.stdout.trim() || pythonVersion.stderr.trim()}`);
await printTool("dotnet", localDotnetExecutablePath(), ["--version"]);

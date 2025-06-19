/* eslint-disable no-control-regex */
/* eslint-disable unicorn/no-hex-escape */
/* eslint-disable unicorn/escape-case */

import { posix as path } from "node:path";

import * as Comlink from "comlink";

import { memoize, stripJsonComments } from "../internal/helpers";
import {
  BebopConfig,
  BuildOptions,
  CommandBuilder,
  CompilerError,
  CompilerException,
  CompilerOutput,
  Diagnostic,
  DiagnosticError,
  Flag,
  FlagValue,
  GeneratedFile,
  GeneratorConfig,
  RootOptions,
  SubCommand,
  WorkerResponse,
} from "./types";

function stripAnsiCodes(message: string): string {
  const regex = /\x1b\[(\d+);5;(\d+)m|\x1b\[0m/g;
  return message.replace(regex, "");
}
// Function to convert ANSI codes to CSS styles and print in browser console
function printAnsiLogToBrowser(message: string): void {
  const regex = /\x1b\[(\d+);5;(\d+)m|\x1b\[0m/g;

  let formattedMessage = message;
  let match: RegExpExecArray | null;
  const cssStyles: string[] = [];

  // Reset the lastIndex to ensure the regex works correctly for global search
  regex.lastIndex = 0;

  while ((match = regex.exec(message)) !== null) {
    if (match[0] === "\x1b[0m") {
      // Handle ANSI reset sequence
      cssStyles.push("color: initial; background-color: initial;");
      formattedMessage = formattedMessage.replace(match[0], "%c");
    } else {
      let style = "";
      const colorType = Number.parseInt(match[1], 10);
      const colorCode = Number.parseInt(match[2], 10);

      let cssColor = "";
      switch (colorCode) {
        case 11:
          cssColor = "yellow";
          break;
        case 12:
          cssColor = "lightblue";
          break;
        case 15:
          cssColor = "white";
          break;
        case 2:
          cssColor = "lightgreen";
          break;
        default:
          cssColor = "initial";
      }

      style = (colorType === 38 ? "color: " : "background-color: ") + cssColor;

      cssStyles.push(style);
      formattedMessage = formattedMessage.replace(match[0], "%c");
    }
  }
  // eslint-disable-next-line no-restricted-globals
  console.log(formattedMessage, ...cssStyles);
}

const spanExitCode = 74;
const ok = 0;
const compilerErrorExitCode = 1;
const fileNotFoundExitCode = 66;

const generatorConfigToString = (config: GeneratorConfig) => {
  const [alias] = Object.keys(config);
  const [body] = Object.values(config);
  const options = Object.entries(body.options || {})
    .map(([key, value]) => `${key}=${value}`)
    .join(",");

  let result = `${alias}:${body.outFile}`;
  if (body.services) {
    result += `,${body.services}`;
  }
  if (body.emitNotice) {
    result += ",emitNotice=true";
  }
  if (body.emitBinarySchema) {
    result += ",emitBinarySchema=true";
  }
  if (body.namespace) {
    result += `,namespace=${body.namespace}`;
  }
  if (options) {
    result += `,${options}`;
  }
  return result;
};

/**
 * Creates a command builder for bebopc.
 * @param parentArgs
 * @returns
 */
const createCommandBuilder = (parentArgs?: string[]) => {
  const args: string[] = parentArgs || ["bebopc"];
  const addFlag = (flag: Flag, value?: FlagValue) => {
    args.push(flag);
    if (value !== undefined) {
      if (Array.isArray(value)) {
        args.push(...value);
      } else {
        args.push(value);
      }
    }
    return { addFlag, addSubCommand, build };
  };

  const addSubCommand = (subCommand: SubCommand) => {
    args.push(subCommand);
    return createCommandBuilder(args);
  };

  const build = () => {
    return args;
  };

  return { addFlag, addSubCommand, build };
};

/*
 * Creates the worker and memoizing it so that it is only created once.
 */
const getWorker = memoize(
  () =>
    new ComlinkWorker<typeof import("./worker")>(
      new URL("worker", import.meta.url),
      { type: "classic" }
    )
);

const addRootOptions = (options: RootOptions, builder: CommandBuilder) => {
  if (options.config) {
    builder.addFlag("--config", options.config);
  }
  if (options.trace) {
    builder.addFlag("--trace");
  }
  if (options.include) {
    builder.addFlag("--include", options.include);
  }
  if (options.exclude) {
    builder.addFlag("--exclude", options.exclude);
  }
  if (options.locale) {
    builder.addFlag("--locale", options.locale);
  }
  if (options.diagnosticFormat) {
    builder.addFlag("--diagnostic-format", options.diagnosticFormat);
  } else {
    builder.addFlag("--diagnostic-format", "json");
  }
};

const fileNameRegexp = /^\s*\/\/\s*@filename:\s*(.+)$/gim;

export const createFileMap = (input: string): Map<string, string> => {
  fileNameRegexp.lastIndex = 0;
  if (!fileNameRegexp.test(input)) {
    throw new CompilerError("error", "no input files found", 1);
  }
  const lines = input.split(/\r?\n/g);
  let currentFilename: string | undefined;
  let currentLines: string[] = [];

  const files = new Map<string, string>();
  function finalizeFile() {
    if (currentFilename) {
      files.set(currentFilename, currentLines.join("\n"));
    }
  }
  for (const line of lines) {
    fileNameRegexp.lastIndex = 0;
    const match = fileNameRegexp.exec(line);
    if (match) {
      finalizeFile();
      currentFilename = path.resolve("/", match[1]);
      currentLines = [];
      continue;
    }

    if (currentFilename) {
      currentLines.push(line);
    }
  }
  finalizeFile();
  return files;
};

const extLookupTable: Record<
  string,
  {
    ext: string;
    auxiliaryExt?: string;
  }
> = {
  cpp: { ext: "cpp", auxiliaryExt: "hpp" },
  cs: { ext: "cs" },
  dart: { ext: "dart" },
  c: { ext: "c" },
  swift: { ext: "swift" },
  py: { ext: "py" },
  rust: { ext: "rs" },
  ts: { ext: "ts" },
};

const createCompilerOutput = (
  files: Map<string, string>,
  configs: GeneratorConfig[],
  stdError: string
): CompilerOutput => {
  if (configs.length === 0) {
    throw new CompilerError("error", "no generators specified", 1);
  }

  let warnings: Diagnostic[] = [];
  let errors: Diagnostic[] = [];
  if (stdError) {
    try {
      const diagnostics = JSON.parse(stdError) as CompilerOutput;
      warnings = diagnostics.warnings;
      errors = diagnostics.errors;
    } catch (e) {
      if (e instanceof Error) {
        throw new CompilerError(
          "error",
          "error while parsing standard error",
          1,
          undefined,
          e
        );
      }

      throw e;
    }
  }

  const results: GeneratedFile[] = [];
  for (const config of configs) {
    const [alias, { outFile }] = Object.entries(config)[0];
    const resolvedOutFile = path.resolve("/", outFile);
    const extMatch = resolvedOutFile.match(/\.([^.]+)$/);
    if (!extMatch) {
      throw new CompilerError("error", "unable to determine extension", 1);
    }

    const extInfo = extLookupTable[alias];
    if (!extInfo) {
      throw new CompilerError(
        "error",
        "unable to lookup extension",
        1,
        undefined,
        { alias }
      );
    }

    const outFileMatch = [...files.keys()].find((f) =>
      f.endsWith(resolvedOutFile)
    );
    if (!outFileMatch) {
      throw new CompilerError(
        "error",
        "unable to find output file",
        1,
        undefined,
        { resolvedOutFile }
      );
    }

    const generatedFile: GeneratedFile = {
      name: outFileMatch,
      content: files.get(outFileMatch) ?? "",
      generator: alias,
    };

    if (extInfo.auxiliaryExt) {
      const outFileDir = path.dirname(resolvedOutFile);
      const auxiliaryFileMatch = [...files.keys()].find((f) => {
        const fileDir = path.dirname(f);
        return f.endsWith(`.${extInfo.auxiliaryExt}`) && fileDir === outFileDir;
      });

      if (auxiliaryFileMatch) {
        generatedFile.auxiliaryFile = {
          name: auxiliaryFileMatch,
          content: files.get(auxiliaryFileMatch) ?? "",
        };
      } else {
        throw new CompilerError(
          "error",
          "unable to find auxiliary file",
          1,
          undefined,
          { resolvedOutFile }
        );
      }
    }

    results.push(generatedFile);
  }

  return { warnings, errors, results };
};
function smoothUpdate(element: HTMLDivElement, start: number, end: number) {
  const duration = 300; // duration of the animation in milliseconds
  const startTime = performance.now();

  function update() {
    const currentTime = performance.now();
    const timeFraction = Math.min((currentTime - startTime) / duration, 1);
    const currentProgress = start + (end - start) * timeFraction;

    element.style.width = `${currentProgress}%`;
    element.textContent = `${Math.round(currentProgress)}%`;

    if (timeFraction < 1) {
      requestAnimationFrame(update);
    }
  }

  requestAnimationFrame(update);
}

async function runBebopc(
  files: Map<string, string>,
  args: string[]
): Promise<WorkerResponse> {
  const worker = getWorker();
  const updateProgress = Comlink.proxy((loaded: number, total: number) => {
    const progressElement = document.querySelector(
      "#progress-bar"
    ) as HTMLDivElement;
    const currentPercent = Number.parseFloat(progressElement.style.width) || 0;
    const newPercent = (loaded / total) * 100;

    smoothUpdate(progressElement, currentPercent, newPercent);
  });
  return worker.runBebopc(files, args, updateProgress);
}

function mapToGeneratorConfigArray(config?: BebopConfig): GeneratorConfig[] {
  if (!config?.generators) {
    return [];
  }
  return Object.entries(config.generators).map(([key, value]) => {
    return { [key]: value } as GeneratorConfig;
  });
}
/**
 * Tries to find any JSON objects in the stdError output and returns them as an array.
 */
const findErrorEntries = (stdError: string): object[] => {
  const validObjects: object[] = [];
  let currentObjectString = "";

  // Split the input string into lines
  const lines = stdError.split("\n");

  for (const line of lines) {
    if (line.trim() === "" && currentObjectString.trim() !== "") {
      // Try to parse the current object string as JSON
      try {
        validObjects.push(JSON.parse(currentObjectString));
      } catch {
        // Ignore parsing errors
      }
      // Reset the current object string for the next JSON object
      currentObjectString = "";
    } else {
      // Add the current line to the current object string
      currentObjectString += line;
    }
  }

  // Check for any remaining JSON object after the last line
  if (currentObjectString.trim() !== "") {
    try {
      validObjects.push(JSON.parse(currentObjectString));
    } catch {
      // Ignore parsing errors
    }
  }

  return validObjects;
};
const isCompilerOutput = (obj: unknown): obj is CompilerOutput => {
  if (!obj || typeof obj !== "object") {
    return false;
  }
  return "warnings" in obj && "errors" in obj;
};

const isCompilerException = (obj: unknown): obj is CompilerException => {
  if (!obj || typeof obj !== "object") {
    return false;
  }
  return "severity" in obj && "message" in obj && "errorCode" in obj;
};

const throwCompilerError = (stdError: string, exitCode: number): void => {
  const errorEntries = findErrorEntries(stdError);
  if (errorEntries && errorEntries.length > 0 && exitCode >= 400) {
    const compilerException = errorEntries.at(-1);
    if (isCompilerException(compilerException)) {
      throw new CompilerError(
        compilerException.severity,
        compilerException.message,
        compilerException.errorCode,
        compilerException.span
      );
    }
  }
  throw new CompilerError("error", stdError, exitCode);
};

const parseStandardError = (
  stdError: string,
  exitCode: number
): CompilerOutput => {
  // we know in our heart of hearts this is a well-formed compiler output
  if (exitCode === 0) {
    return JSON.parse(stdError);
  }

  if (exitCode === 1) {
    const errorEntries = findErrorEntries(stdError);
    if (errorEntries && errorEntries.length > 1) {
      const aggregateErrors: Error[] = [];
      for (const error of errorEntries) {
        if (isCompilerException(error)) {
          aggregateErrors.push(
            new CompilerError(
              error.severity,
              error.message,
              error.errorCode,
              error.span
            )
          );
        } else if (isCompilerOutput(error)) {
          aggregateErrors.push(
            new DiagnosticError([...error.errors, ...error.warnings])
          );
        }
      }
      throw new AggregateError(aggregateErrors, "One or more errors occurred");
    }
    const error = JSON.parse(stdError);
    if (isCompilerException(error)) {
      throw new CompilerError(
        error.severity,
        error.message,
        error.errorCode,
        error.span
      );
    }
    return error as CompilerOutput;
  }
  throw new CompilerError("error", stdError, exitCode);
};

export const BebopCompiler = (
  files?: Map<string, string>,
  options?: RootOptions
) => {
  const fileMap = files ?? new Map<string, string>();
  const builder = createCommandBuilder();
  let bebopConfig: BebopConfig | undefined = undefined;
  if (options) {
    addRootOptions(options, builder);
    if (options.config) {
      const configContent = fileMap.get(options.config);
      if (!configContent) {
        throw new CompilerError("error", "bebop.json not found", 1, undefined, {
          config: options.config,
        });
      }

      bebopConfig = JSON.parse(stripJsonComments(configContent));
    }
  }
  return {
    getHelp: async (): Promise<string> => {
      builder.addFlag("--help");
      const response = await runBebopc(fileMap, builder.build());
      if (response.exitCode !== 0) {
        throwCompilerError(response.stdErr, response.exitCode);
      }
      if (!response.stdOut) {
        throwCompilerError(response.stdErr, response.exitCode);
      }
      return response.stdOut.trim();
    },
    getVersion: async (): Promise<string> => {
      builder.addFlag("--version");
      const response = await runBebopc(fileMap, builder.build());
      if (response.exitCode !== 0) {
        throwCompilerError(response.stdErr, response.exitCode);
      }
      if (!response.stdOut) {
        throwCompilerError(response.stdErr, response.exitCode);
      }
      return response.stdOut.trim();
    },
    init: async (): Promise<BebopConfig> => {
      builder.addFlag("--init");
      const response = await runBebopc(fileMap, builder.build());
      if (response.exitCode !== 0) {
        throwCompilerError(response.stdErr, response.exitCode);
      }
      if (response.newFiles === undefined) {
        throwCompilerError(response.stdErr, response.exitCode);
        throw new Error("response.newFiles is undefined");
      }
      const emittedFiles = createFileMap(response.newFiles);
      for (const [key, value] of emittedFiles.entries()) {
        if (key.endsWith("bebop.json")) {
          return JSON.parse(value);
        }
      }
      throw new CompilerError("error", "bebop.json not found", 1);
    },
    showConfig: async (): Promise<BebopConfig> => {
      builder.addFlag("--show-config");
      const response = await runBebopc(fileMap, builder.build());
      if (response.exitCode !== 0) {
        throwCompilerError(response.stdErr, response.exitCode);
      }
      if (!response.stdOut) {
        throwCompilerError(response.stdErr, response.exitCode);
      }
      return JSON.parse(stripAnsiCodes(response.stdOut));
    },
    listSchemas: async (): Promise<string[]> => {
      builder.addFlag("--list-schemas-only");
      const response = await runBebopc(fileMap, builder.build());
      if (response.exitCode !== 0) {
        throwCompilerError(response.stdErr, response.exitCode);
      }
      if (!response.stdOut) {
        return [];
      }
      return response.stdOut.trim().split(/\r?\n/);
    },
    langServer: async (): Promise<void> => {
      throw new Error("not implemented");
    },
    watch: async (): Promise<void> => {
      throw new Error("not implemented");
    },
    build: async (
      generators?: GeneratorConfig[],
      options?: BuildOptions
    ): Promise<CompilerOutput> => {
      const buildCommand = builder.addSubCommand("build");
      if (generators) {
        for (const generator of generators) {
          buildCommand.addFlag(
            "--generator",
            generatorConfigToString(generator)
          );
        }
      }
      if (options) {
        if (options.noEmit) {
          buildCommand.addFlag("--no-emit");
        }
        if (options.noWarn) {
          buildCommand.addFlag("--no-warn", options.noWarn.map(String));
        }
        if (options.writeToStdOut) {
          buildCommand.addFlag("--stdout");
        }
      }
      const response = await runBebopc(fileMap, buildCommand.build());
      console.log(response)
      if (response.exitCode !== 0) {
        return parseStandardError(response.stdErr, response.exitCode);
      }
      if (options?.noEmit) {
        // if empty likely no errors or warnings
        if (!response.stdErr) {
          return { warnings: [], errors: [] };
        }
        return parseStandardError(response.stdErr, response.exitCode);
      }
      if (!response.newFiles) {
        throwCompilerError(response.stdErr, response.exitCode);
        throw new Error("response.newFiles is undefined");
      }
      if (response.stdOut) {
        printAnsiLogToBrowser(response.stdOut);
      }
      const emittedFiles = createFileMap(response.newFiles);
      return createCompilerOutput(
        emittedFiles,
        generators ?? mapToGeneratorConfigArray(bebopConfig),
        response.stdErr
      );
    },
  };
};

export type TempoService = "none" | "client" | "server" | "both";
export type GeneratorAlias = "cs" | "ts" | "rust" | "py" | "dart" | "cpp" | "c" | "swift";
export type DiagnosticFormat = "json" | "enhanced" | "structured" | "msbuild";

interface GeneratorConfigBody {
  /**
   * Specify a file that bundles all generated code into one file.
   */
  outFile: string;
  /**
   * By default, bebopc generates a concrete client and a service base class. This property can be used to limit bebopc asset generation.
   */
  services?: TempoService;
  /**
   * Specify if the code generator should produce a notice stating code was auto-generated.
   */
  emitNotice?: boolean;
  /**
   * Specify if the code generator should emit a binary schema in the output file that can be used for dynamic serialization.
   */
  emitBinarySchema?: boolean;
  /**
   * Specify a namespace for the generated code.
   */
  namespace?: string;

  /**
   * Specify custom options for the code generator.
   */
  options?: {
    [k: string]: string;
  };
}

export type GeneratorConfig = {
  [K in GeneratorAlias]: { [P in K]: GeneratorConfigBody };
}[GeneratorAlias];

export interface BebopConfig {
  /**
   * Specifies code generators to use for compilation.
   */
  generators?: {
    [K in GeneratorAlias]?: GeneratorConfigBody;
  };
  /**
   * Specifies an array of filenames or patterns to include in the compiler. These filenames are resolved relative to the directory containing the bebop.json file.
   */
  include?: string[];
  /**
   * Specifies an array of filenames or patterns that should be skipped when resolving include. The 'exclude' property only affects the files included via the 'include' property.
   */
  exclude?: string[];

  /**
   * Settings for the watch mode in bebopc.
   */
  watchOptions?: {
    /**
     * Remove a list of files from the watch mode's processing.
     */
    excludeFiles?: string[];
    /**
     * Remove a list of directories from the watch process.
     */
    excludeDirectories?: string[];
  };
  /**
   * Specifies an array of warning codes to silence
   */
  noWarn?: number[];

  /**
   * Disable emitting files from a compilation.
   */
  noEmit?: boolean;

  /**
   * Specify extensions to load.
   */
  extensions?: {
    [k: string]: string;
  };
}

export interface Span {
  fileNames: string;
  startLine: number;
  endLine: number;
  startColumn: number;
  endColumn: number;
  lines: number;
}

export interface Diagnostic {
  message: string;
  errorCode: number;
  severity: "error" | "warning";
  span: Span;
}

export interface AuxiliaryFile {
  name: string;
  content: string;
}

export interface GeneratedFile {
  name: string;
  content: string;
  generator: string;
  auxiliaryFile?: AuxiliaryFile;
}

export interface CompilerOutput {
  warnings: Diagnostic[];
  errors: Diagnostic[];
  results?: GeneratedFile[];
}
export type Severity = "error" | "warning";
export interface CompilerException {
  severity: Severity;
  message: string;
  errorCode: number;
  span?: Span;
}
export class AggregateError extends Error {
  constructor(public errors: Error[]) {
    super("One or more errors occurred");
  }
}
export class DiagnosticError extends Error {
  constructor(public diagnostics: Diagnostic[]) {
    super("One or more diagnostics occurred");
  }
}

export class CompilerError extends Error {
  constructor(
    public severity: Severity,
    public message: string,
    public errorCode: number,
    public span?: Span,
    public cause?: Error | Record<string, unknown>
  ) {
    super(message, cause);
  }

  static emptyStandardOutput(exitCode: number): CompilerError {
    return new CompilerError("error", "Standard output is empty", exitCode);
  }
}

export type Flag = string;
export type FlagValue = string | string[];
export type SubCommand = "build" | "watch" | "langserver";

export interface CommandBuilder {
  addFlag: (flag: Flag, value?: FlagValue) => CommandBuilder;
  addSubCommand: (subCommand: SubCommand) => CommandBuilder;
  build: () => string[];
}

export interface BuildOptions {
  noEmit?: boolean;
  noWarn?: number[];
  readFromStdIn?: boolean;
  writeToStdOut?: boolean;
}

export interface RootOptions {
  config?: string;
  trace?: boolean;
  include?: string[];
  exclude?: string[];
  locale?: string;
  diagnosticFormat?: DiagnosticFormat;
}

export interface WorkerResponse {
  exitCode: number;
  stdErr: string;
  newFiles?: string;
  stdOut: string;
}

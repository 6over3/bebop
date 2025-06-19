import * as lzString from "lz-string";
import editorWorker from "monaco-editor/esm/vs/editor/editor.worker?worker";
import jsonWorker from "monaco-editor/esm/vs/language/json/json.worker?worker";
import jsWorker from "monaco-editor/esm/vs/language/typescript/ts.worker?worker";

import { BebopCompiler, createFileMap } from "./bebopc";
import { CompilerError, Diagnostic, DiagnosticError } from "./bebopc/types";
import exampleStore from "./examples.json" with { type: "json" };
import { monaco } from "./internal/customMonaco.ts";

const configName = "bebop.json";
const envName = ".dev.vars";
const schemaName = "schema.bop";
const v3Prefix = "#v3=";

type EditorFile = {
  name: string;
  content: string;
};

const configureMonaco = () => {
  monaco.languages.register({
    id: "bebop",
    extensions: [".bop"],
    aliases: ["Bebop"],
  });

  monaco.languages.setMonarchTokensProvider("bebop", {
    keywords: [
      "import",
      "const",
      "message",
      "union",
      "struct",
      "enum",
      "true",
      "false",
      "inf",
      "-inf",
      "nan",
      "readonly",
      "mut",
      "service",
      "stream",
      "deprecated",
      "flags",
      "opcode",
      "true",
      "false",
    ],

    typeKeywords: [
      "bool",
      "byte",
      "uint8",
      "uint16",
      "int16",
      "uint32",
      "int32",
      "uint64",
      "int64",
      "float32",
      "float64",
      "string",
      "guid",
      "date"
    ],

    operators: ["=", "->"],

    // we include these common regular expressions
    symbols: /[!%&*+/:<=>?^|~-]+/,

    // C# style strings
    escapes:
      /\\(?:["'\\abfnrtv]|x[\dA-Fa-f]{1,4}|u[\dA-Fa-f]{4}|U[\dA-Fa-f]{8})/,

    // The main tokenizer for our languages
    tokenizer: {
      root: [
        // identifiers and keywords
        [
          /[$_a-z][\w$]*/,
          {
            cases: {
              "@typeKeywords": "keyword",
              "@keywords": "keyword",
              "@default": "identifier",
            },
          },
        ],
        [/[A-Z][\w$]*/, "type.identifier"], // to show class names nicely

        // whitespace
        { include: "@whitespace" },

        // delimiters and operators
        [/[()[\]{}]/, "@brackets"],
        [/[<>](?!@symbols)/, "@brackets"],
        [/@symbols/, { cases: { "@operators": "operator", "@default": "" } }],

        // @ annotations.
        [
          /@\s*[$A-Z_a-z][\w$]*/,
          { token: "annotation" },
        ],

        // numbers
        [/\d*\.\d+([Ee][+-]?\d+)?/, "number.float"],
        [/0[Xx][\dA-Fa-f]+/, "number.hex"],
        [/\d+/, "number"],

        // delimiter: after number because of .\d floats
        [/[,.;]/, "delimiter"],

        // strings
        [/"([^"\\]|\\.)*$/, "string.invalid"], // non-teminated string
        [/"/, { token: "string.quote", bracket: "@open", next: "@string" }],

        // characters
        [/'[^'\\]'/, "string"],
        [/(')(@escapes)(')/, ["string", "string.escape", "string"]],
        [/'/, "string.invalid"],
      ],

      comment: [
        [/[^*/]+/, "comment"],
        [/\/\*/, "comment", "@push"], // nested comment
        ["\\*/", "comment", "@pop"],
        [/[*/]/, "comment"],
      ],

      string: [
        [/[^"\\]+/, "string"],
        [/'+/, "string"],
        [/@escapes/, "string.escape"],
        [/\\./, "string.escape.invalid"],
        [/"/, { token: "string.quote", bracket: "@close", next: "@pop" }],
      ],

      whitespace: [
        [/[\t\n\r ]+/, "white"],
        [/\/\*/, "comment", "@comment"],
        [/\/\/.*$/, "comment"],
      ],
    },
  });

  monaco.languages.json.jsonDefaults.setDiagnosticsOptions({
    validate: true,
    allowComments: true,
    trailingCommas: "ignore",
    enableSchemaRequest: true,
    schemas: [
      {
        uri: `${document.location.protocol}//${document.location.host}/assets/bebop-schema.json`,
        fileMatch: ["bebop.json"],
      },
    ],
    schemaValidation: "error"
  });
};

const createEditor = (
  container: string,
  language: string,
  label: string,
  content: string = ""
) => {
  const editorContainer = document.querySelector(
    `#${container}`
  ) as HTMLElement;
  if (editorContainer === null) {
    throw new Error(`Could not find editor container ${container}`);
  }
  const editorElement = editorContainer.querySelector(`.editor`) as HTMLElement;

  if (editorElement === null) {
    throw new Error(`Could not find editor element within ${container}`);
  }

  const labelElement = editorContainer.querySelector(
    `.editor-label`
  ) as HTMLElement;
  if (labelElement === null) {
    throw new Error(`Could not find editor label within ${container}`);
  }

  //labelElement.textContent = label;

  const model = monaco.editor.createModel(
    content,
    language,
    monaco.Uri.parse(`file:///${label}`)
  );

  const editor = monaco.editor.create(editorElement, {
    model,
    minimap: {
      enabled: false,
    },
    readOnly: label === "preview",
    domReadOnly: label === "preview",
    //readOnly: false,
    theme: "vs-dark",
    formatOnPaste: true,
    formatOnType: true,
  });
  editor.updateOptions({ wordWrap: "on" });


  return {
    editor,
    updateLanguage: (language: string) => {
      const model = editor.getModel();
      if (model) {
        monaco.editor.setModelLanguage(model, language);
      }
    },
    setMarkers: (markers: monaco.editor.IMarkerData[]) => {
      const model = editor.getModel();
      if (model) {
        monaco.editor.setModelMarkers(model, language, markers);
      }
    },
    disableErrors: () => {
      const model = editor.getModel();
      if (model) {
        monaco.editor.setModelMarkers(model, "error", []);
      }
    },
    clearMarkers: () => {
      monaco.editor.removeAllMarkers(language);
    },
    setValue: (content: string) => {
      editor.setValue(content.trim());
      editor.setScrollPosition({scrollTop: 0});
    },
    getValue: () => {
      return editor.getValue().trim();
    },
    hasErrors: () => {
      const model = editor.getModel();
      if (model) {
        const markers = monaco.editor.getModelMarkers({
          resource: model.uri,
        });
        return markers.some((f) => f.severity === monaco.MarkerSeverity.Error);
      }
      return false;
    },
    hasWarnings: () => {
      const model = editor.getModel();
      if (model) {
        const markers = monaco.editor.getModelMarkers({
          resource: model.uri,
        });
        return markers.some(
          (f) => f.severity === monaco.MarkerSeverity.Warning
        );
      }
      return false;
    },
    setLabel: (label: string) => {
      labelElement.textContent = label;
    },
    getFile: (): EditorFile => {
      const model = editor.getModel();
      if (model) {
        return {
          name: model.uri.fsPath.slice(1),
          content: model.getValue().trim(),
        };
      }
      throw new Error(`Could not find model`);
    },
  };
};

const importSchema = `
/** This record was imported by another schema */
struct Example {
    string data;
    guid id;
    date createdAt;
}
const string greetings = "I am a const from an imported schema";
`

const createBebopCompiler = (files?: EditorFile[]) => {
  let initialContent = "";
  if (files !== undefined) {
    for (const file of files) { 
      initialContent += `// @filename: ${file.name}`;
      initialContent += "\n";
      initialContent += file.content;
      initialContent += "\n";
      if (file.name === schemaName && file.content.includes("example.bop")) {
        initialContent += "// @filename: example.bop";
        initialContent += "\n";
        initialContent += importSchema;
        initialContent += "\n";
      }
    }
    const fileMap = createFileMap(initialContent);
    return BebopCompiler(fileMap, {
      config: "/bebop.json",
      trace: true,
    });
  }

  return BebopCompiler();
};

export const populateExamples = () => {
  for (const [k, v] of Object.entries(exampleStore.examples.v3)) {
    const examples = document.querySelector(
      "body > header > nav > ul > li.relative.group > div"
    );
    if (examples === null) {
      throw new Error(`Could not find examples element`);
    }
    const warningText = `'${v.name}' example will be loaded to the playground. Your current changes will be lost.`;
    const exampleElement = document.createElement("a");
    exampleElement.dataset.modalText = warningText;
    exampleElement.className = "confirm-link block px-4 py-1 hover:bg-gray-600";
    exampleElement.href = `${v3Prefix}${v.data}`;
    exampleElement.textContent = v.name;
    examples.append(exampleElement);
  }
};

export const init = async () => {

  self.MonacoEnvironment = {
    getWorker: function (_, label) {
      switch (label) {
        case "json":
          return new jsonWorker();
        case "typescript":
        case "javascript":
          return new jsWorker();
        default:
          return new editorWorker();
      }
    },
  };
  configureMonaco();
  const playground = {
    schema: createEditor("schema-container", "bebop", schemaName),
    env: createEditor("vars-container", "ini", envName),
    config: createEditor("config-container", "json", configName),
    preview: createEditor("preview-container", "typescript", "preview"),
    layout: () => {
      playground.schema.editor.layout();
      playground.env.editor.layout();
      playground.config.editor.layout();
      playground.preview.editor.layout();
    },
    getFiles: (): EditorFile[] => {
      return [
        playground.schema.getFile(),
        playground.env.getFile(),
        playground.config.getFile(),
      ];
    },
    saveState: () => {
      let files = "";
      for (const file of playground.getFiles()) {
        files += `// @filename: ${file.name}`;
        files += "\n";
        files += file.content.trim();
        files += "\n";
      }

      const value = v3Prefix + lzString.compressToEncodedURIComponent(files);
      const valueWithHash = value.startsWith("#") ? value : `#${value}`;
      window.location.hash = valueWithHash;
    },
    loadState: (state?: string) => {
      if (state === undefined) {
        state = window.location.hash;
      }

      if (!state.startsWith(v3Prefix) || window.location.hash === "") {
        state = `${v3Prefix}${exampleStore.examples.v3.helloworld.data}`;
      }

      state = state.slice(v3Prefix.length);
      try {
        const decompressedState =
          lzString.decompressFromEncodedURIComponent(state);
        const parsed = createFileMap(decompressedState);
        let schemaContent = "";
        let envContent = "";
        let configContent = "";
        for (const [file, content] of parsed.entries()) {
          if (typeof file === "string" && typeof content === "string") {
            switch (file.slice(1)) {
              case schemaName:
                schemaContent = content;
                break;
              case envName:
                envContent = content;
                break;
              case configName:
                configContent = content;
                break;
            }
          }
        }
        // avoid triggering change events until all files are loaded
        playground.env.setValue(envContent);
        playground.config.setValue(configContent);
        playground.schema.setValue(schemaContent);
         
      } catch (e) {
        // eslint-disable-next-line no-restricted-globals
        console.error(e);
        playground.loadState(
          `${v3Prefix}${exampleStore.examples.v3.helloworld.data}`
        );
      }
    },
    updatePreview: async (content: string, languageId: string) => {
      playground.preview.updateLanguage(languageId);
      playground.preview.editor.updateOptions({
        readOnly: false,
      });
      playground.preview.setValue(content);
      playground.preview.clearMarkers();
      await playground.preview.editor
        .getAction("editor.action.formatDocument")
        ?.run();
      playground.preview.editor.updateOptions({
        readOnly: true,
      });
      playground.preview.editor.setScrollPosition({scrollTop: 0});
    },
  };
  playground.loadState();

  const versionLabel = document.querySelector("body > footer");
  if (versionLabel) {
    versionLabel.textContent = `Version ${await createBebopCompiler().getVersion()}`;
  }
  const setMarkers = async (diagnostics: Diagnostic[]) => {
    playground.schema.clearMarkers();
    const markers = diagonsticsToMarkers(diagnostics);
    if (markers.length > 0) {
      playground.schema.setMarkers(markers);
    }
  };
  const tryBuild = async () => {
    if (playground.config.hasErrors() || playground.config.hasWarnings()) {
      return;
    }
    const compiler = createBebopCompiler(playground.getFiles());
    try {
      const output = await compiler.build();
      await setMarkers([...output.warnings, ...output.errors]);
      if (playground.schema.hasErrors()) {
        await playground.updatePreview("", "text");
        return;
      }
      if (output.results !== null && output.results !== undefined) {
        for (const result of output.results) {
          switch (result.generator) {
            case "ts":
              await playground.updatePreview(result.content, "typescript");
              break;
            case "py":
              await playground.updatePreview(result.content, "python");
              break;
            case "rust":
              await playground.updatePreview(result.content, "rust");
              break;
            case "cs":
              await playground.updatePreview(result.content, "csharp");
              break;
            case "dart":
              await playground.updatePreview(result.content, "dart");
              break;
              case "c":
              await playground.updatePreview(result.content, "cpp");
              break;
            case "cpp":
              await playground.updatePreview(result.content, "cpp");
              break;
          }
        }
      }
    } catch (e) {
      if (e instanceof AggregateError) {
        for (const error of e.errors) {
          if (error instanceof DiagnosticError) {
            await setMarkers(error.diagnostics);
          } else if (error instanceof CompilerError) {
            await playground.updatePreview(error.message, "text");
          }
        }
      } else if (e instanceof CompilerError) {
        await playground.updatePreview(e.message, "text");
      } else if (e instanceof Error) {
        await playground.updatePreview(e.toString(), "text");
      }
    }
  };
  playground.config.editor.onDidChangeModelContent(() => {
    playground.saveState();
  });
  playground.env.editor.onDidChangeModelContent(() => {
    playground.saveState();
  });
  playground.schema.editor.onDidChangeModelContent(async () => {
    playground.saveState();
    await tryBuild();
  });

  //populateLanguages(playground);
  await tryBuild();

  const overlay = document.querySelector("#main-overlay");
  if (overlay) {
    overlay.classList.add("hidden");
  }

  window.addEventListener("resize", () => {
    playground.layout();
  });
  populateExamples();

  return playground;
};

function diagonsticsToMarkers(diagnostics: Diagnostic[]) {
  const markers = [];
  for (const diagnostic of diagnostics) {
    const range = new monaco.Range(
      diagnostic.span.startLine + 1,
      diagnostic.span.startColumn + 1,
      diagnostic.span.endLine + 1,
      diagnostic.span.endColumn + 1
    );
    markers.push({
      severity:
        diagnostic.severity === "error"
          ? monaco.MarkerSeverity.Error
          : monaco.MarkerSeverity.Warning,
      message: diagnostic.message,
      startLineNumber: range.startLineNumber,
      startColumn: range.startColumn,
      endLineNumber: range.endLineNumber,
      endColumn: range.endColumn,
    });
  }
  return markers;
}

export const debounce = <F extends (...args: Parameters<F>) => ReturnType<F>>(
  fn: F,
  delay: number
) => {
  let timeout: ReturnType<typeof setTimeout>;
  return function (...args: Parameters<F>) {
    clearTimeout(timeout);
    timeout = setTimeout(() => {
      fn.apply(args);
    }, delay);
  };
};

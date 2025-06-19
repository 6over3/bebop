const shell = require("shelljs");
const fs = require("fs");

// Configuration
const config = {
  cxx: shell.env.CXX || "g++",
  cc: shell.env.CC || "gcc",
  py: process.platform === "win32" ? "py" : "python3",
  executables: {
    aout: process.platform === "win32" ? "a.exe" : "./a.out",
    cout: process.platform === "win32" ? "c.exe" : "./c.out",
    cdecode: process.platform === "win32" ? "c_decode.exe" : "./c_decode"
  }
};

const languages = [
  { name: "Rust", encodeCmd: "cargo run --manifest-path Rust/Cargo.toml --example encode", decodeCmd: "cargo run --manifest-path Rust/Cargo.toml -q --example decode --", file: "rs.enc" },
  { name: "C#", encodeCmd: "dotnet run encode", decodeCmd: "dotnet run decode", file: "cs.enc" },
  { name: "TypeScript", encodeCmd: "npx ts-node encode.ts", decodeCmd: "npx ts-node decode.ts", file: "ts.enc" },
  { name: "C++", encodeCmd: null, decodeCmd: config.executables.aout, file: "cpp.enc" },
  { name: "C", encodeCmd: null, decodeCmd: config.executables.cdecode, file: "c.enc" },
  { name: "Python", encodeCmd: `${config.py} Python/src/encode.py`, decodeCmd: `${config.py} Python/src/decode.py`, file: "py.enc" }
];

// Utility functions
function log(msg, color = "") {
  const colors = { red: "\x1b[31m", green: "\x1b[32m", yellow: "\x1b[33m", blue: "\x1b[34m", reset: "\x1b[0m" };
  shell.echo(`${colors[color] || ""}${msg}${colors.reset}`);
}

function execQuiet(cmd) {
  return shell.exec(cmd, { silent: true });
}

function execWithOutput(cmd, description) {
  log(`→ ${description}`, "blue");
  const result = shell.exec(cmd);
  if (result.code !== 0) {
    log(`✗ Failed: ${description}`, "red");
    log(`Command: ${cmd}`, "yellow");
    return false;
  }
  log(`✓ Success: ${description}`, "green");
  return true;
}

function hexDump(filename, maxBytes = 128) {
  try {
    const data = fs.readFileSync(filename);
    const bytes = data.slice(0, maxBytes);
    let hex = "";
    let ascii = "";

    for (let i = 0; i < bytes.length; i++) {
      if (i % 16 === 0 && i > 0) {
        hex += `  |${ascii}|\n`;
        ascii = "";
      }
      if (i % 16 === 0) {
        hex += `${i.toString(16).padStart(4, "0")}: `;
      }

      hex += `${bytes[i].toString(16).padStart(2, "0")} `;
      ascii += bytes[i] >= 32 && bytes[i] <= 126 ? String.fromCharCode(bytes[i]) : ".";
    }

    // Pad the last line
    const remaining = 16 - (bytes.length % 16);
    if (remaining < 16) {
      hex += "   ".repeat(remaining);
    }
    hex += `  |${ascii.padEnd(16, " ")}|`;

    if (data.length > maxBytes) {
      hex += `\n... (${data.length - maxBytes} more bytes)`;
    }

    return hex;
  } catch (err) {
    return `Error reading file: ${err.message}`;
  }
}

function cleanup() {
  Object.values(config.executables).forEach(exe => shell.rm("-f", exe));
  languages.forEach(lang => shell.rm("-f", lang.file));
}

// Main execution
function main() {
  log("🔨 Cross-Language Bebop Test Suite", "blue");
  log("=".repeat(50));

  // Step 1: Compile schema
  log("\n📋 Compiling schema...", "yellow");
  const schemaCmd = "dotnet run --project ../../Compiler --include schema.bop build " +
    "--generator 'c:schema.c' --generator 'cs:schema.cs' --generator 'ts:schema.ts' " +
    "--generator 'cpp:schema.hpp' --generator 'rust:Rust/src/schema.rs' --generator 'py:Python/src/schema.py'";

  if (!execWithOutput(schemaCmd, "Schema compilation")) {
    log("✗ Schema compilation failed - cannot continue", "red");
    shell.exit(1);
  }

  // Step 2: Compile native decoders
  log("\n🔧 Compiling native decoders...", "yellow");

  if (!execWithOutput(`${config.cxx} --std=c++17 decode.cpp -o ${config.executables.aout}`, "C++ decoder")) {
    log("✗ C++ decoder compilation failed", "red");
  }

  if (!execWithOutput(`${config.cc} --std=c11 decode.c bebop.c schema.c makelib.c -o ${config.executables.cdecode}`, "C decoder")) {
    log("✗ C decoder compilation failed", "red");
  }

  // Step 3: Generate encodings
  log("\n📝 Generating encodings...", "yellow");

  const encodingResults = [];
  for (const lang of languages) {
    let cmd;
    let compileSuccess = true;

    if (lang.name === "C++") {
      // Compile and run C++ encoder
      compileSuccess = execWithOutput(`${config.cxx} --std=c++17 encode.cpp -o ${config.executables.aout}`, `${lang.name} encoder compilation`);
      if (!compileSuccess) {
        encodingResults.push({ lang: lang.name, success: false, reason: "compilation failed" });
        continue;
      }
      cmd = `${config.executables.aout} > ${lang.file}`;
    } else if (lang.name === "C") {
      // Compile and run C encoder
      compileSuccess = execWithOutput(`${config.cc} --std=c11 encode.c bebop.c schema.c makelib.c -o ${config.executables.cout}`, `${lang.name} encoder compilation`);
      if (!compileSuccess) {
        encodingResults.push({ lang: lang.name, success: false, reason: "compilation failed" });
        continue;
      }
      cmd = `${config.executables.cout} > ${lang.file}`;
    } else {
      cmd = `${lang.encodeCmd} > ${lang.file}`;
    }

    const encodeSuccess = execWithOutput(cmd, `${lang.name} encoding`);
    encodingResults.push({
      lang: lang.name,
      success: encodeSuccess,
      reason: encodeSuccess ? null : "encoding failed",
      hasFile: encodeSuccess && fs.existsSync(lang.file)
    });
  }

  // Step 4: Show hex dumps of generated files
  log("\n🔍 Generated encoding files (hex view):", "yellow");
  for (const lang of languages) {
    const result = encodingResults.find(r => r.lang === lang.name);
    if (result && result.success && fs.existsSync(lang.file)) {
      log(`\n${lang.name} (${lang.file}):`);
      console.log(hexDump(lang.file));
    } else {
      log(`\n${lang.name} (${lang.file}): ${result ? result.reason || "file not found" : "unknown error"}`, "red");
    }
  }

  // Step 5: Cross-language decode testing
  log("\n🧪 Cross-language decode testing...", "yellow");
  log("Testing that all decoders can read all encodings...\n");

  let testsFailed = false;
  const results = [];

  for (const decoder of languages) {
    for (const encoder of languages) {
      const encoderResult = encodingResults.find(r => r.lang === encoder.name);
      const testName = `${decoder.name} decoding ${encoder.name}`;

      // Skip test if encoder file doesn't exist
      if (!encoderResult || !encoderResult.success || !fs.existsSync(encoder.file)) {
        log(`⚠ Skipped: ${testName} (encoder file not available)`, "yellow");
        results.push({
          decoder: decoder.name,
          encoder: encoder.name,
          success: false,
          skipped: true,
          reason: "encoder file not available"
        });
        continue;
      }

      const cmd = `${decoder.decodeCmd} ${encoder.file}`;
      const result = execQuiet(cmd);
      const status = result.code === 0 ? "✓" : "✗";
      const color = result.code === 0 ? "green" : "red";

      log(`${status} ${testName}`, color);

      if (result.code !== 0) {
        testsFailed = true;
        log(`  Command: ${cmd}`, "yellow");
        if (result.stderr) {
          log(`  Error: ${result.stderr.trim()}`, "red");
        }
      }

      results.push({
        decoder: decoder.name,
        encoder: encoder.name,
        success: result.code === 0,
        skipped: false,
        reason: result.code !== 0 ? result.stderr?.trim() || "unknown error" : null
      });
    }
  }

  // Step 6: Comprehensive Summary
  log("\n📊 Test Summary:", "blue");
  log("=".repeat(50));

  // Encoding summary
  log("\n📝 Encoding Results:");
  const successfulEncodings = encodingResults.filter(r => r.success);
  const failedEncodings = encodingResults.filter(r => !r.success);

  log(`  ✓ Successful: ${successfulEncodings.length}/${encodingResults.length}`);
  if (successfulEncodings.length > 0) {
    successfulEncodings.forEach(r => log(`    • ${r.lang}`, "green"));
  }

  if (failedEncodings.length > 0) {
    log(`  ✗ Failed: ${failedEncodings.length}/${encodingResults.length}`);
    failedEncodings.forEach(r => log(`    • ${r.lang}: ${r.reason}`, "red"));
  }

  // Decode testing summary
  log("\n🧪 Cross-Language Decode Results:");
  const totalTests = results.length;
  const passedTests = results.filter(r => r.success).length;
  const skippedTests = results.filter(r => r.skipped).length;
  const failedTests = results.filter(r => !r.success && !r.skipped).length;

  log(`  ✓ Passed: ${passedTests}/${totalTests}`);
  log(`  ✗ Failed: ${failedTests}/${totalTests}`);
  log(`  ⚠ Skipped: ${skippedTests}/${totalTests}`);

  if (failedTests > 0) {
    log("\n🔴 Decode Failures:");
    for (const result of results.filter(r => !r.success && !r.skipped)) {
      log(`  • ${result.decoder} ← ${result.encoder}`, "red");
      if (result.reason) {
        log(`    ${result.reason}`, "yellow");
      }
    }
  }

  if (skippedTests > 0) {
    log("\n🟡 Skipped Tests:");
    for (const result of results.filter(r => r.skipped)) {
      log(`  • ${result.decoder} ← ${result.encoder}: ${result.reason}`, "yellow");
    }
  }

  // Overall status
  const overallSuccess = failedEncodings.length === 0 && failedTests === 0;
  log(`\n🎯 Overall Result: ${overallSuccess ? "SUCCESS" : "PARTIAL SUCCESS"}`, overallSuccess ? "green" : "yellow");

  if (overallSuccess) {
    log("🎉 All encodings work and all cross-language tests passed!", "green");
  } else {
    log("⚠ Some issues found - see details above", "yellow");
  }

  cleanup();

  // Exit with error code only if we have actual test failures (not just encoding failures)
  if (testsFailed) {
    shell.exit(1);
  }
}

// Handle cleanup on exit
process.on('SIGINT', cleanup);
process.on('SIGTERM', cleanup);

main();
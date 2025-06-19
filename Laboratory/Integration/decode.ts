import { Library } from "./schema";
import { makelib } from "./makelib";
import fs from "node:fs";
import equal from "deep-equal";
import { assert } from "node:console";
import util from "node:util";

const buffer = fs.readFileSync(process.argv[2]);
const de = Library.decode(buffer);
const ex = makelib();

// JSON.stringify/parse automatically strips functions
const deJson = JSON.parse(JSON.stringify(de));
const exJson = JSON.parse(JSON.stringify(ex));

const eq = equal(deJson, exJson, { strict: false });

if (!eq) {
    console.log("decoded:");
    console.log(util.inspect(deJson, { showHidden: false, depth: null }));
    console.log();
    console.log("expected:");
    console.log(util.inspect(exJson, { showHidden: false, depth: null }));
}
process.exit(eq ? 0 : 1);
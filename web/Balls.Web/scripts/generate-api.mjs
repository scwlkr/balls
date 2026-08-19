import { readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

import openapiTS, { astToString } from "openapi-typescript";
import prettier from "prettier";

const schema = new URL(
  "../../../docs/protocol/local-control-v1.openapi.json",
  import.meta.url,
);
const output = fileURLToPath(
  new URL("../src/api/generated/local-control-v1.ts", import.meta.url),
);
const generated = await prettier.format(astToString(await openapiTS(schema)), {
  parser: "typescript",
});

if (process.argv.includes("--check")) {
  const committed = await readFile(output, "utf8");
  if (committed !== generated) {
    console.error(
      "Generated local-control client is stale. Run `pnpm web:generate`.",
    );
    process.exitCode = 1;
  }
} else {
  await writeFile(output, generated, "utf8");
  console.log(`Generated ${output}`);
}

const assert = require("node:assert/strict");
const { spawnSync } = require("node:child_process");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

if (!process.env.PATH) throw new Error("Run through task docs:links:test.");
const modulePaths = process.env.PATH.split(path.delimiter).map((entry) =>
  path.resolve(entry, ".."),
);
const cli = modulePaths
  .map((entry) => path.join(entry, "remark-cli", "cli.js"))
  .find((entry) => fs.existsSync(entry));
assert.ok(cli, "The pinned remark CLI must be available through npx.");
const repositoryRoot = path.resolve(__dirname, "..");
const root = fs.mkdtempSync(path.join(os.tmpdir(), "bathos-markdown-links-"));
const repository = "https://github.com/bathos-labs/platform-dotnet";
const readme = `# Alpha

[local](#alpha)
[reference][target]
[root](/docs/space%20name.md#install--run)
[repository](${repository}/blob/master/docs/space%20name.md#install--run)
![local image](docs/icon%20name.svg)
[external](https://unreachable.invalid/no-file#no-heading)
[external-encoding](https://unreachable.invalid/#invalid-%escape)
[duplicate](#same-1)
[Unicode](#résumé)
[encoded Unicode](#r%C3%A9sum%C3%A9)
[inline heading](#use-widget--go)

| Capability | Guide |
| --- | --- |
| Link | [target](docs/space%20name.md#install--run) |

\`[inline example](missing-inline.md)\`

\`\`\`markdown
[ignored](missing-code-example.md)
# Fenced heading
\`\`\`

## Same

## Same

## Résumé

## Use \`Widget\` & go!

[target]: docs/space%20name.md#install--run
`;

function check() {
  const result = spawnSync(
    process.execPath,
    [
      cli,
      "--rc-path",
      path.join(repositoryRoot, ".remarkrc.cjs"),
      "--ignore-path",
      ".prettierignore",
      "--frail",
      "--no-stdout",
      "--no-color",
      ".",
    ],
    { cwd: root, encoding: "utf8" },
  );
  if (result.error) throw result.error;
  assert.notEqual(result.status, null, `Link checker terminated: ${result.signal}`);
  return { status: result.status, output: result.stdout + result.stderr };
}

try {
  fs.copyFileSync(path.join(repositoryRoot, ".prettierignore"), path.join(root, ".prettierignore"));
  fs.mkdirSync(path.join(root, "docs"));
  fs.writeFileSync(path.join(root, "docs", "space name.md"), "# Target\n\n## Install & run\n");
  fs.writeFileSync(
    path.join(root, "docs", "icon name.svg"),
    '<svg xmlns="http://www.w3.org/2000/svg"/>',
  );
  fs.writeFileSync(path.join(root, "README.md"), readme);
  for (const ignored of ["artifacts", "obj", "bin", "node_modules"]) {
    fs.mkdirSync(path.join(root, ignored));
    fs.writeFileSync(
      path.join(root, ignored, "README.md"),
      "[broken](missing-generated-file.md)\n",
    );
  }
  const valid = check();
  assert.equal(valid.status, 0, valid.output);
  assert.equal(fs.readFileSync(path.join(root, "README.md"), "utf8"), readme);

  const invalidLinks = [
    ["[missing file](docs/missing.md)", "missing-file"],
    ["![missing image](docs/missing.svg)", "missing-file"],
    ["[missing heading](#not-a-heading)", "missing-heading"],
    ["[missing cross-file heading](docs/space%20name.md#not-a-heading)", "missing-heading"],
    [`[missing repository file](${repository}/blob/master/docs/missing.md)`, "missing-file"],
    ["[undefined reference][absent]", "no-undefined-references"],
    ["[code is not a heading](#fenced-heading)", "missing-heading"],
    ["[invalid encoding](#bad-%escape)", "Invalid URL encoding"],
  ];
  for (const [markdown, diagnostic] of invalidLinks) {
    fs.writeFileSync(path.join(root, "README.md"), readme + "\n" + markdown + "\n");
    const invalid = check();
    assert.notEqual(invalid.status, 0, `Accepted invalid Markdown: ${markdown}`);
    assert.ok(invalid.output.includes("README.md"), invalid.output);
    assert.ok(invalid.output.includes(diagnostic), invalid.output);
  }
  fs.writeFileSync(path.join(root, "README.md"), readme);
  const repaired = check();
  assert.equal(repaired.status, 0, repaired.output);
  console.log(
    "Documentation link checks passed valid GFM, encoding, exclusions, and eight failure/repair cases.",
  );
} finally {
  fs.rmSync(root, { recursive: true, force: true });
}

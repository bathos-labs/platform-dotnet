const path = require("node:path");

// npx exposes its temporary packages through PATH rather than this repository's node_modules.
if (!process.env.PATH)
  throw new Error("Run documentation link checks through the pinned npx command.");
const modulePaths = process.env.PATH.split(path.delimiter).map((entry) =>
  path.resolve(entry, ".."),
);
const dependency = (name) => require.resolve(name, { paths: modulePaths });
const repository = "https://github.com/bathos-labs/platform-dotnet";

// remark-validate-links decodes file paths but compares fragments with literal heading IDs.
function decodeLocalFragments() {
  return (tree, file) => {
    function visit(node) {
      if (typeof node.url === "string") {
        const local = !/^[a-z][a-z0-9+.-]*:/i.test(node.url);
        const hosted = node.url.startsWith(`${repository}/blob/`);
        const fragment = node.url.indexOf("#");
        if ((local || hosted) && fragment !== -1) {
          try {
            node.url =
              node.url.slice(0, fragment + 1) + decodeURIComponent(node.url.slice(fragment + 1));
          } catch {
            file.fail(`Invalid URL encoding in link fragment: ${node.url}`, node);
          }
        }
      }
      if (Array.isArray(node.children)) node.children.forEach(visit);
    }
    visit(tree);
  };
}

module.exports = {
  plugins: [
    dependency("remark-gfm"),
    dependency("remark-lint-no-undefined-references"),
    decodeLocalFragments,
    [dependency("remark-validate-links"), { repository }],
  ],
};

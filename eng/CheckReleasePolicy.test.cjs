const assert = require("node:assert/strict");
const { spawnSync } = require("node:child_process");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

const repository = path.resolve(__dirname, "..");
const checker = path.join(
  repository,
  "eng",
  "Bathos.Platform.Build",
  "bin",
  "Release",
  "net10.0",
  "Bathos.Platform.Build.dll",
);
assert.ok(fs.existsSync(checker), "Build the release checker first, or run task release:test.");
const scratch = fs.mkdtempSync(path.join(os.tmpdir(), "bathos release policy "));
const env = { ...process.env };
for (const key of Object.keys(env)) if (key.startsWith("GIT_")) delete env[key];
env.GIT_CONFIG_NOSYSTEM = "1";
env.GIT_CONFIG_GLOBAL = path.join(scratch, "empty.gitconfig");
env.GIT_ALLOW_PROTOCOL = "file";
env.GIT_TERMINAL_PROMPT = "0";
fs.writeFileSync(env.GIT_CONFIG_GLOBAL, "");
const origin = path.join(scratch, "origin.git");
const work = path.join(scratch, "work");
fs.mkdirSync(work);
let assertions = 0;

function execute(executable, args, cwd = work) {
  const result = spawnSync(executable, args, { cwd, env, encoding: "utf8" });
  if (result.error) throw result.error;
  assert.notEqual(
    result.status,
    null,
    `${executable} terminated (${result.signal}): ${result.stderr}`,
  );
  return { status: result.status, output: result.stdout + result.stderr };
}

function git(...args) {
  const result = execute("git", args);
  assert.equal(result.status, 0, result.output);
  return result.output.trim();
}

function prepare(version, branch, date = "2026-10-25") {
  git("reset", "--hard");
  git("switch", "-C", branch, "origin/master");
  fs.writeFileSync(
    path.join(work, "Directory.Build.props"),
    `<Project><PropertyGroup><Version>${version}</Version></PropertyGroup></Project>\n`,
  );
  fs.writeFileSync(path.join(work, "CHANGELOG.md"), `# Changelog\n\n## [${version}] - ${date}\n`);
  git("add", ".");
  git("commit", "--allow-empty", "-m", `Prepare ${branch} ${version}`);
  return `v${version}`;
}

function check(tag, remote, accepted, diagnostic) {
  const result = execute("dotnet", [checker, "verify-tag", tag, ...(remote ? ["--remote"] : [])]);
  assert.equal(result.status === 0, accepted, result.output);
  if (diagnostic) assert.ok(result.output.includes(diagnostic), result.output);
  assertions++;
}

try {
  git("init", "--bare", origin);
  git("init", "--initial-branch=master");
  git("config", "user.name", "Release policy fixture");
  git("config", "user.email", "fixture@example.invalid");
  git("config", "core.autocrlf", "false");
  git("remote", "add", "origin", origin);
  fs.writeFileSync(path.join(work, "Bathos.Platform.slnx"), "<Solution />\n");
  fs.writeFileSync(
    path.join(work, "Directory.Build.props"),
    "<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>\n",
  );
  fs.writeFileSync(path.join(work, "CHANGELOG.md"), "# Changelog\n\n## [1.0.0] - 2026-10-25\n");
  git("add", ".");
  git("commit", "-m", "Initial stable source");
  git("push", "-u", "origin", "master");

  for (const version of ["1.2.0", "1.2.0-beta.1", "1.2.0-rc.1", "1.2.0+build-beta"]) {
    const tag = prepare(version, "master");
    check(tag, false, true, "Release preparation accepted");
    git("tag", "-a", tag, "-m", "Fixture annotated release");
    check(tag, true, false, "must be"); // A local master commit is not remote publication evidence.
    git("push", "origin", "master");
    git("switch", "--detach", "HEAD");
    check(tag, false, false, "checked-out master or release/*");
    check(tag, true, true, "origin/master");
  }

  for (const branch of ["release/1.3", "release/nested/candidate"]) {
    const version = branch.includes("nested") ? "1.3.0-rc.2+build-beta" : "1.3.0-beta.2";
    const tag = prepare(version, branch);
    check(tag, false, true);
    git("tag", tag);
    check(tag, true, false, "must be pushed");
    git("push", "origin", branch);
    git("switch", "--detach", "HEAD");
    check(tag, true, true, `origin/${branch}`);
    git("switch", branch);
    fs.appendFileSync(path.join(work, "CHANGELOG.md"), "\nNew development\n");
    check(tag, true, false, "clean checkout");
    git("restore", "CHANGELOG.md");
    git("commit", "--allow-empty", "-m", "Advance beyond the tag");
    check(tag, true, false, "tagged commit");
    git("switch", "--detach", tag);
    git("push", "origin", "--delete", branch);
    // Reintroduce stale tracking evidence; the checker must prune it during its own fetch.
    git("update-ref", `refs/remotes/origin/${branch}`, "HEAD");
    check(tag, true, false, "must be pushed");
  }

  for (const version of ["1.4.0", "1.4.1+build-beta"]) {
    const stable = prepare(version, "release/1.4");
    check(stable, false, false, "Only master");
    git("tag", stable);
    git("push", "origin", "release/1.4");
    git("switch", "--detach", stable);
    check(stable, true, false, "origin/master");
    git("switch", "master");
    git("merge", "--ff-only", "release/1.4");
    git("push", "origin", "master");
    git("switch", "--detach", stable);
    check(stable, true, true, "origin/master");
  }

  for (const [index, branch] of [
    "feature/next",
    "releases/next",
    "release-candidate",
    "preview/next",
  ].entries()) {
    for (const suffix of ["", "-beta.1"]) {
      const tag = prepare(`2.${index}.${suffix ? 1 : 0}${suffix}`, branch);
      check(tag, false, false, "Only master");
      git("tag", tag);
      git("push", "--force", "origin", branch);
      git("switch", "--detach", tag);
      check(tag, true, false, suffix ? "must be pushed" : "origin/master");
    }
  }

  for (const version of [
    "01.2.0-beta.1",
    "1.2.0-01",
    "1.2.0-",
    "1.2.0-beta..1",
    "1.2",
    "1.2.0+metadata..part",
  ]) {
    check(prepare(version, "master"), false, false, "Semantic Versioning");
  }
  const tag = prepare("3.0.0-beta.1", "release/3.0");
  check("v3.0.0", false, false, "Release tag must be");
  fs.writeFileSync(path.join(work, "CHANGELOG.md"), "# Changelog\n");
  check(tag, false, false, "dated CHANGELOG.md entry");
  prepare("3.0.0-beta.1", "release/3.0", "2026-02-30");
  check(tag, false, false, "dated CHANGELOG.md entry");
  const unavailable = prepare("3.0.0-beta.2", "release/3.0");
  git("tag", unavailable);
  git("push", "origin", "release/3.0");
  git("remote", "set-url", "origin", path.join(scratch, "missing-origin.git"));
  check(unavailable, true, false, "Release Git verification failed");
  // Case-distinct remote refs cannot coexist reliably on case-insensitive fixture filesystems.
  check(prepare("4.0.0-beta.1", "Release/case-probe"), false, false, "Only master");
  assert.equal(git("--git-dir", origin, "for-each-ref", "--format=%(refname)", "refs/tags"), "");
  console.log(
    `Release policy passed ${assertions} preparation/publication checks against isolated local Git repositories; no release tags were pushed.`,
  );
} finally {
  fs.rmSync(scratch, { recursive: true, force: true });
}

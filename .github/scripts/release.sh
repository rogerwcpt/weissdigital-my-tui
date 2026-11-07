#!/usr/bin/env bash
# Publish self-contained my-tui binaries and create the next patch release.
#
# The highest vMAJOR.MINOR.PATCH tag wins, and this script adds one to the
# patch. With no such tag, the first release is v0.1.0. To open a new minor
# or major, create that tag (v0.2.0, v1.0.0). The next push continues from it.
#
# GitHub Actions supplies GITHUB_TOKEN. gh uses that. There is no personal
# access token in this script.
#
# Ghostty.Vt ships native code for osx-arm64 and linux-x64 only. The macOS
# archive also carries libmytui_pty.dylib, which has to stay next to my-tui.
#
# The macOS runner's /bin/bash is 3.2. Keep this script compatible with it.

set -euo pipefail

RIDS=(osx-arm64 linux-x64)
asset_total=$(( ${#RIDS[@]} + 1 ))

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
work_root=${RUNNER_TEMP:-${TMPDIR:-/tmp}}
work_root=${work_root%/}
publish_root="$work_root/mytui-publish"
dist_root="$work_root/mytui-dist"
published_version=""

die() {
  printf '%s\n' "$*" >&2
  exit 1
}

binary_name() {
  printf '%s\n' "my-tui"
}

archive_name() {
  local version=$1
  local rid=$2
  local ver=${version#v}
  printf 'my-tui-%s-%s.tar.gz\n' "$ver" "$rid"
}

# Empty input starts the history at v0.1.0.
bump_version() {
  local latest=${1:-}
  local patch
  if [ -z "$latest" ]; then
    printf '%s\n' "v0.1.0"
    return 0
  fi
  if [[ ! "$latest" =~ ^v([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]; then
    printf 'Unusable version tag: %s\n' "$latest" >&2
    return 1
  fi
  # 10# keeps a hand-made tag such as v0.1.08 in decimal. Bash would
  # otherwise read a leading zero as octal.
  patch=$((10#${BASH_REMATCH[3]} + 1))
  printf 'v%s.%s.%s\n' "${BASH_REMATCH[1]}" "${BASH_REMATCH[2]}" "$patch"
}

compute_next_version() {
  local tag latest=""
  while IFS= read -r tag; do
    if [[ "$tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
      latest=$tag
      break
    fi
  done < <(git tag --list 'v*' --sort=-v:refname)
  bump_version "$latest"
}

tag_pointing_at_head() {
  local tag
  while IFS= read -r tag; do
    if [[ "$tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
      printf '%s\n' "$tag"
      return 0
    fi
  done < <(git tag --points-at "$GITHUB_SHA" --list 'v*' --sort=-v:refname)
}

install_notes() {
  local version=$1
  local short=$2
  local ver=${version#v}
  cat <<EOF
Self-contained build of ${version}, from \`${short}\`. The archive includes the runtime.

| Archive | Machine |
| --- | --- |
| \`my-tui-${ver}-osx-arm64.tar.gz\` | Apple silicon |
| \`my-tui-${ver}-linux-x64.tar.gz\` | Linux, 64-bit |

Each archive contains the program and the MIT license. The Apple silicon archive also contains \`libmytui_pty.dylib\`. Unpack the archive and put that folder on your PATH. On macOS the dylib has to stay next to \`my-tui\`.

Check the download against SHA256SUMS: \`shasum -a 256 -c SHA256SUMS\`, or \`sha256sum -c SHA256SUMS\`.

A browser download on macOS is quarantined by Gatekeeper. The binary is ad-hoc signed. Clear the quarantine with \`xattr -d com.apple.quarantine my-tui\`.
EOF
}

write_checksums() {
  local dist=$1
  python3 - "$dist" <<'PY'
import hashlib
import pathlib
import sys

dist = pathlib.Path(sys.argv[1])
lines = []
for path in sorted(p for p in dist.iterdir() if p.is_file() and p.name != "SHA256SUMS"):
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    lines.append(f"{digest}  {path.name}")
(dist / "SHA256SUMS").write_text("\n".join(lines) + "\n")
PY
}

package_release() {
  local version=$1
  local publish_dir=$2
  local dist=$3
  local rid stage name binary members
  rm -rf "$dist"
  mkdir -p "$dist"
  for rid in "${RIDS[@]}"; do
    stage=$(mktemp -d)
    name=$(archive_name "$version" "$rid")
    binary=$(binary_name "$rid")
    cp "$publish_dir/$rid/$binary" "$stage/$binary" || return 1
    cp "$repo_root/LICENSE" "$stage/LICENSE" || return 1
    members="$binary LICENSE"
    if [ "$rid" = "osx-arm64" ]; then
      cp "$publish_dir/$rid/libmytui_pty.dylib" "$stage/libmytui_pty.dylib" || return 1
      members="$binary libmytui_pty.dylib LICENSE"
    fi
    chmod +x "$stage/$binary" || return 1
    # COPYFILE_DISABLE keeps AppleDouble files out of archives built on macOS.
    # shellcheck disable=SC2086
    COPYFILE_DISABLE=1 tar -C "$stage" -czf "$dist/$name" $members || return 1
    rm -rf "$stage"
  done
  write_checksums "$dist" || return 1
}

# Version is known only after the tag is chosen. A retry that has to take the
# next patch publishes again, so the binary's version matches the release.
publish_binaries() {
  local version=$1
  local ver rid binary extra
  if [ "$published_version" = "$version" ]; then
    return 0
  fi
  ver=${version#v}
  rm -rf "$publish_root"
  mkdir -p "$publish_root"
  for rid in "${RIDS[@]}"; do
    printf 'Publishing %s\n' "$rid"
    dotnet publish "$repo_root/src/MyTui" \
      --configuration Release \
      --runtime "$rid" \
      --self-contained true \
      -p:PublishSingleFile=true \
      -p:IncludeNativeLibrariesForSelfExtract=true \
      -p:EnableCompressionInSingleFile=true \
      -p:Version="$ver" \
      -p:InformationalVersion="$ver" \
      -p:IncludeSourceRevisionInInformationalVersion=false \
      -p:DebugType=none \
      -p:DebugSymbols=false \
      --output "$publish_root/$rid" || return 1
    binary=$(binary_name "$rid")
    if [ ! -s "$publish_root/$rid/$binary" ]; then
      printf 'Publish for %s did not produce %s.\n' "$rid" "$binary" >&2
      return 1
    fi
    if [ "$rid" = "osx-arm64" ] && [ ! -s "$publish_root/$rid/libmytui_pty.dylib" ]; then
      printf 'Publish for %s did not produce libmytui_pty.dylib.\n' "$rid" >&2
      return 1
    fi
    # A pdb is harmless because only the program is packed. The macOS shim is
    # the one native file that has to stay beside my-tui.
    if [ "$rid" = "osx-arm64" ]; then
      extra=$(find "$publish_root/$rid" -maxdepth 1 -type f ! -name "$binary" ! -name 'libmytui_pty.dylib' ! -name '*.pdb' -print)
    else
      extra=$(find "$publish_root/$rid" -maxdepth 1 -type f ! -name "$binary" ! -name '*.pdb' -print)
    fi
    if [ -n "$extra" ]; then
      printf 'Publish for %s left files beside %s:\n%s\n' "$rid" "$binary" "$extra" >&2
      return 1
    fi
  done
  published_version=$version
}

fetch_tags() {
  git fetch --tags --force --quiet origin
}

release_is_complete() {
  local version=$1
  local answer
  answer=$(gh release view "$version" --repo "$GITHUB_REPOSITORY" \
    --json isDraft,assets \
    --jq "if .isDraft == false and (.assets | length) >= ${asset_total} then \"yes\" else \"no\" end" \
    2>/dev/null || true)
  [ "$answer" = "yes" ]
}

remove_release() {
  local version=$1
  if gh release view "$version" --repo "$GITHUB_REPOSITORY" >/dev/null 2>&1; then
    gh release delete "$version" --repo "$GITHUB_REPOSITORY" --yes --cleanup-tag || return 1
  else
    gh api -X DELETE "repos/${GITHUB_REPOSITORY}/git/refs/tags/${version}" >/dev/null || return 1
  fi
  git tag -d "$version" >/dev/null 2>&1 || true
}

# Prints nothing and returns 10 when this commit is already published.
# Returns 0 when the commit is free to publish.
reconcile_existing() {
  local existing
  fetch_tags || return 1
  existing=$(tag_pointing_at_head)
  if [ -z "$existing" ]; then
    return 0
  fi
  if release_is_complete "$existing"; then
    printf 'This commit is already published as %s.\n' "$existing"
    return 10
  fi
  printf 'Removing unfinished release %s.\n' "$existing"
  remove_release "$existing" || return 1
  fetch_tags || return 1
  if git rev-parse -q --verify "refs/tags/${existing}" >/dev/null; then
    printf 'Tag %s is still present after removal.\n' "$existing" >&2
    return 1
  fi
  return 0
}

write_notes() {
  local version=$1
  local file=$2
  local short generated
  short=$(printf '%s' "$GITHUB_SHA" | cut -c1-7)
  generated=$(gh api --method POST "repos/${GITHUB_REPOSITORY}/releases/generate-notes" \
    -f tag_name="$version" \
    -f target_commitish="$GITHUB_SHA" \
    --jq .body) || return 1
  if [ "$generated" = "null" ]; then
    generated=""
  fi
  {
    install_notes "$version" "$short"
    printf '\n'
    printf '%s\n' "$generated"
  } > "$file"
}

create_release_once() {
  local status version notes
  reconcile_existing
  status=$?
  if [ "$status" -eq 10 ]; then
    return 0
  fi
  if [ "$status" -ne 0 ]; then
    return 1
  fi
  version=$(compute_next_version) || return 1
  printf 'Publishing %s\n' "$version"
  publish_binaries "$version" || return 1
  notes=$(mktemp)
  write_notes "$version" "$notes" || return 1
  package_release "$version" "$publish_root" "$dist_root" || return 1
  gh release create "$version" \
    --repo "$GITHUB_REPOSITORY" \
    --target "$GITHUB_SHA" \
    --title "$version" \
    --notes-file "$notes" \
    "$dist_root"/* || return 1
  rm -f "$notes"
}

publish_with_retry() {
  local attempt=1
  local status
  while [ "$attempt" -le 5 ]; do
    # set -e stays off for the attempt so a lost race can try the next patch.
    # macOS bash 3.2 would otherwise exit the script on the first failure.
    set +e
    create_release_once
    status=$?
    set -e
    if [ "$status" -eq 0 ]; then
      return 0
    fi
    printf 'Release attempt %s failed.\n' "$attempt"
    sleep $((attempt * 3))
    attempt=$((attempt + 1))
  done
  return 1
}

self_check() {
  local got tmp fake dist rid binary text
  command -v python3 >/dev/null 2>&1 || die "python3 is required."
  command -v tar >/dev/null 2>&1 || die "tar is required."
  command -v git >/dev/null 2>&1 || die "git is required."

  got=$(bump_version "")
  [ "$got" = "v0.1.0" ] || die "empty tag list should start at v0.1.0, got $got"
  got=$(bump_version "v0.1.0")
  [ "$got" = "v0.1.1" ] || die "v0.1.0 should bump to v0.1.1, got $got"
  got=$(bump_version "v0.1.9")
  [ "$got" = "v0.1.10" ] || die "v0.1.9 should bump to v0.1.10, got $got"
  got=$(bump_version "v1.2.9")
  [ "$got" = "v1.2.10" ] || die "v1.2.9 should bump to v1.2.10, got $got"
  if bump_version "v1.2" >/dev/null 2>&1; then
    die "v1.2 should be rejected."
  fi

  text=$(install_notes "v0.1.0" "abc1234")
  case "$text" in
    *'my-tui-0.1.0-osx-arm64.tar.gz'*) ;;
    *) die "Install notes are missing the archive name." ;;
  esac
  case "$text" in
    *'libmytui_pty.dylib'*) ;;
    *) die "Install notes dropped the macOS shim." ;;
  esac

  tmp=$(mktemp -d)
  git init -q -b main "$tmp"
  git -C "$tmp" config user.email "mytui-release@example.com"
  git -C "$tmp" config user.name "my-tui release self-check"
  git -C "$tmp" -c commit.gpgsign=false commit -q --allow-empty -m init
  (
    cd "$tmp"
    got=$(compute_next_version)
    [ "$got" = "v0.1.0" ] || die "a repo with no tags should start at v0.1.0, got $got"
    git tag v0.1.0
    git tag v0.1.2
    git tag v0.1.10
    git tag not-a-version
    got=$(compute_next_version)
    [ "$got" = "v0.1.11" ] || die "highest tag should win, got $got"
  )

  fake=$(mktemp -d)
  dist=$(mktemp -d)
  for rid in "${RIDS[@]}"; do
    mkdir -p "$fake/$rid"
    binary=$(binary_name "$rid")
    printf 'binary-%s' "$rid" > "$fake/$rid/$binary"
    if [ "$rid" = "osx-arm64" ]; then
      printf 'shim' > "$fake/$rid/libmytui_pty.dylib"
    fi
  done
  package_release "v9.9.9" "$fake" "$dist" || die "Packaging the self-check binaries failed."
  python3 - "$dist" <<'PY'
import hashlib
import pathlib
import sys
import tarfile

dist = pathlib.Path(sys.argv[1])
archives = sorted(p for p in dist.iterdir() if p.name != "SHA256SUMS")
if len(archives) != 2:
    raise SystemExit(f"expected 2 archives, found {archives}")
sums = {}
for line in (dist / "SHA256SUMS").read_text().splitlines():
    digest, name = line.split("  ", 1)
    sums[name] = digest
if set(sums) != {p.name for p in archives}:
    raise SystemExit(f"checksum names {sorted(sums)} != {[p.name for p in archives]}")
for path in archives:
    actual = hashlib.sha256(path.read_bytes()).hexdigest()
    if sums[path.name] != actual:
        raise SystemExit(f"bad checksum for {path.name}")
with tarfile.open(dist / "my-tui-9.9.9-osx-arm64.tar.gz") as tar:
    if set(tar.getnames()) != {"my-tui", "libmytui_pty.dylib", "LICENSE"}:
        raise SystemExit(f"unexpected tar members {tar.getnames()}")
    member = tar.getmember("my-tui")
    if member.mode & 0o111 == 0:
        raise SystemExit(f"my-tui is not executable: {oct(member.mode)}")
    payload = tar.extractfile(member).read()
    if payload != b"binary-osx-arm64":
        raise SystemExit(f"unexpected my-tui payload {payload!r}")
    if tar.extractfile("libmytui_pty.dylib").read() != b"shim":
        raise SystemExit("unexpected dylib payload")
with tarfile.open(dist / "my-tui-9.9.9-linux-x64.tar.gz") as tar:
    if set(tar.getnames()) != {"my-tui", "LICENSE"}:
        raise SystemExit(f"unexpected linux tar members {tar.getnames()}")
print("package self-check ok")
PY
  rm -rf "$tmp" "$fake" "$dist"
  printf 'Release self-check passed.\n'
}

require_release_context() {
  if [ "${GITHUB_ACTIONS:-}" != "true" ] && [ "${MYTUI_RELEASE:-}" != "1" ]; then
    die "Refusing to publish. The release workflow runs this on a push to main."
  fi
  if [ -z "${GITHUB_REPOSITORY:-}" ] || [ -z "${GITHUB_SHA:-}" ]; then
    die "GITHUB_REPOSITORY and GITHUB_SHA are required."
  fi
  if [ -z "${GH_TOKEN:-}" ]; then
    if [ -n "${GITHUB_TOKEN:-}" ]; then
      GH_TOKEN=$GITHUB_TOKEN
      export GH_TOKEN
    else
      die "GH_TOKEN is not set. The workflow passes github.token."
    fi
  fi
  export GH_REPO="$GITHUB_REPOSITORY"
  export GH_PROMPT_DISABLED=1
  export GH_NO_UPDATE_NOTIFIER=1
  if [ "$(git rev-parse HEAD)" != "$GITHUB_SHA" ]; then
    die "HEAD is $(git rev-parse HEAD), expected ${GITHUB_SHA}."
  fi
}

main() {
  cd "$repo_root"
  if [ "${1:-}" = "--self-check" ]; then
    self_check
    exit 0
  fi
  require_release_context
  self_check
  command -v dotnet >/dev/null 2>&1 || die "dotnet is required."
  command -v gh >/dev/null 2>&1 || die "gh is required."
  publish_with_retry || die "Could not publish a release."
}

main "$@"

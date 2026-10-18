#!/usr/bin/env bash
# Installs the latest docker-credential-acr build from the main branch on macOS or Linux.
# Requires the GitHub CLI (gh), signed in with `gh auth login`.
set -euo pipefail

REPO="afscrome/AcrCredentialHelper"
INSTALL_DIR="$HOME/.acrcredhelper"

case "$(uname -s)" in
  Darwin) os="osx" ;;
  Linux) os="linux" ;;
  *) echo "Unsupported operating system: $(uname -s)" >&2; exit 1 ;;
esac

case "$(uname -m)" in
  x86_64 | amd64) arch="x64" ;;
  arm64 | aarch64) arch="arm64" ;;
  *) echo "Unsupported architecture: $(uname -m)" >&2; exit 1 ;;
esac

run_id="$(gh run list --repo "$REPO" --workflow build.yml --branch main --event push \
  --status success --limit 1 --json databaseId --jq '.[0].databaseId')"

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

echo "Downloading ${os}-${arch} build from run $run_id..."
gh run download "$run_id" --repo "$REPO" --name "${os}-${arch}-PublishArtifacts" --dir "$work_dir"

# The executable and the native msalruntime library used by the Microsoft authentication broker must sit together.
mkdir -p "$INSTALL_DIR"
release_dir="$(dirname "$(find "$work_dir" -type f -name docker-credential-acr | head -n 1)")"
install -m 755 "$release_dir/docker-credential-acr" "$INSTALL_DIR/"
for lib in "$release_dir"/*msalruntime*; do
  [ -f "$lib" ] && install -m 755 "$lib" "$INSTALL_DIR/"
done

# Allow macOS to run the downloaded files.
if [ "$os" = "osx" ]; then
  xattr -d com.apple.quarantine "$INSTALL_DIR"/docker-credential-acr "$INSTALL_DIR"/*msalruntime* 2>/dev/null || true
fi

echo "Installed to $INSTALL_DIR."

# Add the install directory to the user's PATH if it is missing.
case ":$PATH:" in
  *":$INSTALL_DIR:"*) ;;
  *)
    case "$(basename "${SHELL:-}")" in
      zsh) profile="$HOME/.zshrc" ;;
      bash) if [ "$os" = "osx" ]; then profile="$HOME/.bash_profile"; else profile="$HOME/.bashrc"; fi ;;
      *) profile="" ;;
    esac
    export_line="export PATH=\"$INSTALL_DIR:\$PATH\""
    if [ -n "$profile" ]; then
      grep -qsF "$export_line" "$profile" || printf '\n# Added by the ACR credential helper installer\n%s\n' "$export_line" >> "$profile"
      echo "Added $INSTALL_DIR to your PATH in $profile. Restart your shell to pick it up."
    else
      echo "Add $INSTALL_DIR to your PATH, for example: $export_line"
    fi
    ;;
esac

echo "Next, run: docker-credential-acr install"

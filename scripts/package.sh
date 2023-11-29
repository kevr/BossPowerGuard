#!/bin/bash
SCRIPT_DIR="$(dirname "${0}")"
PROJECT_DIR="${SCRIPT_DIR}/.."

FILES_DIR="${PROJECT_DIR}/files"
mkdir -p "$FILES_DIR"

files=(
    manifest.json
    icon.png
    README.md
    CHANGELOG.md
    bin/Release/net461/BossPowerGuard.dll
    bin/Release/net461/ServerSync.dll
)

for f in ${files[@]}; do
    cp -v "$f" "${FILES_DIR}/"
done

echo
VERSION=$(grep version ${FILES_DIR}/manifest.json | sed -r 's;"version_number": "([0-9.]+)",;\1;g' | awk '{print $1}')
if [ -f ${FILES_DIR}/Kevver-BossPowerGuard-${VERSION}.zip ]; then
    echo "Version ${VERSION} is already packaged."
else
    echo "Version ${VERSION} isn't packaged yet!"
fi

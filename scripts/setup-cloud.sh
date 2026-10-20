#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_dir="$(cd -- "${script_dir}/.." && pwd)"
source "${script_dir}/env-cloud.sh"
sdk_version=10.0.402
sdk_sha512=1904a98dda2db34ba78ba3c4fb1b224455329af91c341d83547843c1839e112b04ae699ee92fa2449a8f62249196a938723078c0c60b9f65c88539d5687729c2
if [[ ! -x "${DOTNET_ROOT}/dotnet" ]] || [[ "$("${DOTNET_ROOT}/dotnet" --version)" != "${sdk_version}" ]]; then
    sdk_archive="$(mktemp /tmp/nexus-sdk-XXXXXXXX.tar.gz)"
    trap 'rm -f -- "${sdk_archive}"' EXIT
    curl --fail --silent --show-error --location --retry 2 \
      "https://builds.dotnet.microsoft.com/dotnet/Sdk/${sdk_version}/dotnet-sdk-${sdk_version}-linux-x64.tar.gz" \
      --output "${sdk_archive}"
    echo "${sdk_sha512}  ${sdk_archive}" | sha512sum --check --status
    mkdir -p "${DOTNET_ROOT}"
    tar -xzf "${sdk_archive}" -C "${DOTNET_ROOT}"
fi
cd "${repo_dir}"
dotnet restore NexusOptimizer.sln --locked-mode -m:1 -nr:false
dotnet build NexusOptimizer.sln -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test tests/NexusOptimizer.Tests -c Release --no-build --no-restore \
  --logger 'trx;LogFileName=core-tests.trx' --results-directory artifacts/test-results

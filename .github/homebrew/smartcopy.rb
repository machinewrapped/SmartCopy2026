cask "smartcopy" do
  version "0.0.0"
  sha256 "0000000000000000000000000000000000000000000000000000000000000000"

  url "https://github.com/machinewrapped/SmartCopy2026/releases/download/v#{version}/SmartCopy-#{version}-osx-arm64.tar.gz"
  name "SmartCopy"
  desc "Intelligently copies large directories via composable filters and transform pipelines"
  homepage "https://github.com/machinewrapped/SmartCopy2026"

  binary "SmartCopy"
end

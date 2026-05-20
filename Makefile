SHELL := powershell.exe
.SHELLFLAGS := -NoProfile -ExecutionPolicy Bypass -Command

.DEFAULT_GOAL := build

CONFIG ?= Release
PLATFORM ?= x86
SIMHUB_PATH ?= C:/Program Files (x86)/SimHub
MOZA_SDK_PATH ?=

SOLUTION := MozaDevicesPlugin.sln
OUTPUT_DIR := bin/$(PLATFORM)/$(CONFIG)

PLUGIN_DLL := $(OUTPUT_DIR)/MozaDevicesPlugin.dll
MOZA_CSHARP_DLL := $(OUTPUT_DIR)/MOZA_API_CSharp.dll
MOZA_C_DLL := $(OUTPUT_DIR)/MOZA_API_C.dll
MOZA_SDK_DLL := $(OUTPUT_DIR)/MOZA_SDK.dll

.PHONY: deps check-deps build deploy rebuild clean print-output

deps:
	$$args = @("-SimHubPath", "$(SIMHUB_PATH)"); if ("$(MOZA_SDK_PATH)" -ne "") { $$args += @("-MozaSdkPath", "$(MOZA_SDK_PATH)") }; & ".\scripts\Prepare-Dependencies.ps1" @args

check-deps:
	$$ErrorActionPreference = "Stop"; foreach ($$file in @("libs/SimHub/SimHub.Plugins.dll", "libs/SimHub/GameReaderCommon.dll", "libs/SimHub/SimHub.Logging.dll", "libs/SimHub/Newtonsoft.Json.dll", "libs/MOZA_SDK/MOZA_API_CSharp.dll", "libs/MOZA_SDK/MOZA_API_C.dll", "libs/MOZA_SDK/MOZA_SDK.dll")) { if (-not (Test-Path -LiteralPath $$file)) { throw "Missing dependency: $$file. Run make deps MOZA_SDK_PATH='path/to/SDK_CSharp/x86'." } }

build: check-deps
	dotnet build "$(SOLUTION)" -c "$(CONFIG)" -p:Platform="$(PLATFORM)"

deploy: build
	$$ErrorActionPreference = "Stop"; $$dest = "$(SIMHUB_PATH)"; if (-not (Test-Path -LiteralPath $$dest)) { throw "SIMHUB_PATH does not exist: $$dest" }; foreach ($$file in @("$(PLUGIN_DLL)", "$(MOZA_CSHARP_DLL)", "$(MOZA_C_DLL)", "$(MOZA_SDK_DLL)")) { if (-not (Test-Path -LiteralPath $$file)) { throw "Missing deploy file: $$file" }; Copy-Item -LiteralPath $$file -Destination $$dest -Force; Write-Host "Copied $$file -> $$dest" }

rebuild: check-deps
	dotnet build "$(SOLUTION)" -c "$(CONFIG)" -p:Platform="$(PLATFORM)" -t:Rebuild

clean:
	dotnet clean "$(SOLUTION)" -c "$(CONFIG)" -p:Platform="$(PLATFORM)"

print-output:
	Write-Host "Output files:"; @("$(PLUGIN_DLL)", "$(MOZA_CSHARP_DLL)", "$(MOZA_C_DLL)", "$(MOZA_SDK_DLL)") | ForEach-Object { Write-Host "  $$_" }; Write-Host "Deploy target: $(SIMHUB_PATH)"

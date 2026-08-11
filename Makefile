PYTHON ?= python3
DOTNET ?= dotnet
CARGO ?= cargo
RUNTIME_MOD_PROJECTS := $(sort $(wildcard mods/*/*.csproj))

install:
	$(CARGO) fetch --manifest-path installer-rs/Cargo.toml

doctor:
	$(PYTHON) tools/patch_sneak_out.py --list-mods

mods-build:
	@for project in $(RUNTIME_MOD_PROJECTS); do $(DOTNET) build "$$project" || exit 1; done

installer-test:
	$(CARGO) test --manifest-path installer-rs/Cargo.toml --all-features

installer-build:
	$(CARGO) build --manifest-path installer-rs/Cargo.toml --release --features gui --bins

installer-build-dev:
	$(CARGO) build --manifest-path installer-rs/Cargo.toml --release --features gui,dev-mode --bins

installer-payload:
	$(PYTHON) tools/package_installer_payload.py

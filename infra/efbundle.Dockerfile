# Wraps the self-contained executable produced by `dotnet ef migrations bundle`
# (see .github/workflows/build.yml). Not a project publish output, so it can't
# go through the SDK's PublishContainer path the Api/Web images use — needs a
# real Dockerfile. runtime-deps only: the bundle is self-contained and embeds
# its own .NET runtime.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0

WORKDIR /app
COPY efbundle .

ENTRYPOINT ["./efbundle"]

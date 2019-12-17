FROM alpine AS gitCloner
RUN apk --update --no-cache --quiet add git openssh
WORKDIR /project
RUN git clone https://github.com/rosenbjerg/BlockCoverageGeneratorWeb.git

FROM mcr.microsoft.com/dotnet/core/sdk:3.0-alpine AS backendBuilder
WORKDIR /app
COPY --from=gitCloner /project/BlockCoverageGeneratorWeb/ReportGeneratorServer/ ./
RUN dotnet publish ReportGeneratorServer.csproj -c Release -r linux-musl-x64 -o build

FROM mcr.microsoft.com/dotnet/core/aspnet:3.0-alpine AS runtime
LABEL maintainer="Malte Rosenbjerg"
LABEL description="coverage-gen-web on dotnet 3.0"
LABEL repository="github.com/rosenbjerg/BlockCoverageGeneratorWeb"
WORKDIR /app
COPY --from=frontendBuilder /app/build ./public/
ENV NXPLX_BUILD=$build
EXPOSE 5353
ENTRYPOINT ["dotnet", "ReportGeneratorServer.dll"]
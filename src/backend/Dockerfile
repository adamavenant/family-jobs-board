# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source

COPY src/backend/Directory.Build.props src/backend/Directory.Packages.props src/backend/
COPY src/backend/FamilyJobsBoard.sln src/backend/
COPY src/backend/FamilyJobsBoard.Domain/FamilyJobsBoard.Domain.csproj src/backend/FamilyJobsBoard.Domain/
COPY src/backend/FamilyJobsBoard.Application/FamilyJobsBoard.Application.csproj src/backend/FamilyJobsBoard.Application/
COPY src/backend/FamilyJobsBoard.Infrastructure/FamilyJobsBoard.Infrastructure.csproj src/backend/FamilyJobsBoard.Infrastructure/
COPY src/backend/FamilyJobsBoard.Api/FamilyJobsBoard.Api.csproj src/backend/FamilyJobsBoard.Api/
COPY src/backend/FamilyJobsBoard.Migrations/FamilyJobsBoard.Migrations.csproj src/backend/FamilyJobsBoard.Migrations/
COPY src/backend/FamilyJobsBoard.Api.IntegrationTests/FamilyJobsBoard.Api.IntegrationTests.csproj src/backend/FamilyJobsBoard.Api.IntegrationTests/
RUN dotnet restore src/backend/FamilyJobsBoard.sln

COPY src/backend src/backend
RUN dotnet publish src/backend/FamilyJobsBoard.Api/FamilyJobsBoard.Api.csproj \
    --no-restore --configuration Release --output /out/api /p:UseAppHost=false
RUN dotnet publish src/backend/FamilyJobsBoard.Migrations/FamilyJobsBoard.Migrations.csproj \
    --no-restore --configuration Release --output /out/migrate /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS api
WORKDIR /app
RUN apk add --no-cache krb5-libs tzdata
COPY --from=build /out/api .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "FamilyJobsBoard.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS migrate
WORKDIR /app
RUN apk add --no-cache krb5-libs tzdata
COPY --from=build /out/migrate .
USER app
ENTRYPOINT ["dotnet", "FamilyJobsBoard.Migrations.dll"]

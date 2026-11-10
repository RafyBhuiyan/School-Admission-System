FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:1a48394b6f29d3a32c79d9edecd4abda0ecbe8776d567dbfc4590ed6979c740c AS base
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
WORKDIR /src
COPY global.json ./
COPY .config/dotnet-tools.json .config/dotnet-tools.json
COPY Backend/SchoolAdmission.Api/SchoolAdmission.Api.csproj Backend/SchoolAdmission.Api/packages.lock.json Backend/SchoolAdmission.Api/
RUN dotnet restore Backend/SchoolAdmission.Api/SchoolAdmission.Api.csproj --locked-mode \
    && dotnet tool restore
COPY Backend/SchoolAdmission.Api/ Backend/SchoolAdmission.Api/
WORKDIR /src/Backend/SchoolAdmission.Api
RUN dotnet publish --configuration Release --no-restore --output /app/publish /p:UseAppHost=false
RUN ConnectionStrings__DefaultConnection="Host=localhost;Database=design_time" \
    dotnet ef migrations bundle --configuration Release --no-build --output /app/migrate

FROM base AS final
COPY --from=build /app/publish/ ./
COPY --from=build --chmod=755 /app/migrate ./migrate
ENTRYPOINT ["dotnet", "SchoolAdmission.Api.dll"]

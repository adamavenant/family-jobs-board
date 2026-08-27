# Platform Foundation Specification

## Overview
This document defines the technical baseline and architecture for the Family Jobs Board application, establishing a consistent development platform with clear guidelines and configurations.

## Technology Stack

### Backend (.NET)
- **Framework**: .NET 10
- **Database**: PostgreSQL 18
- **ORM**: Entity Framework Core with Npgsql provider
- **API Documentation**: Swagger/OpenAPI
- **Logging**: Serilog
- **Testing**: xUnit, FluentAssertions

### Frontend (React)
- **Framework**: React 19
- **Build Tool**: Vite
- **TypeScript**: Enabled by default
- **Package Manager**: npm

## Architecture

### Modular Monolith Approach
This project follows the modular monolith architectural pattern as defined in ADR 0002:
- Domain Layer: Contains core business logic and entities
- Application Layer: Contains use cases and application services
- Infrastructure Layer: Contains database context, external service integrations
- API Layer: Contains controllers and HTTP endpoints

### Folder Structure
```
src/
├── backend/                 # Backend components
│   ├── FamilyJobsBoard.Api/    # API layer
│   ├── FamilyJobsBoard.Application/  # Application layer  
│   ├── FamilyJobsBoard.Domain/       # Domain layer
│   └── FamilyJobsBoard.Infrastructure/ # Infrastructure layer
├── web/                     # Web application
│   ├── public/              # Static files
│   └── src/                 # Source code
└── tests/                   # Test projects
```

## Build Configuration

### Dependency Management
- Centralized package versions via `Directory.Packages.props`
- Package version pinning for consistent builds using centralized version management
- Dependencies managed through NuGet (for .NET) and npm (for JavaScript)

### Build Properties  
- `Directory.Build.props` containing common build configurations
- Target Framework: .NET 8.0
- Nullable reference types enabled
- Implicit using directives enabled

## Development Process

### Environment Setup
1. Install .NET 8.0 SDK
2. Install Node.js LTS
3. Install PostgreSQL 18
4. Configure database connection string in `appsettings.json`

### Testing Strategy
- Unit tests in individual layer projects (Domain, Application, Infrastructure)
- Integration tests in dedicated test projects
- End-to-end testing for API endpoints

## Docker Integration

### Services Configuration
All services are configured to run within a containerized environment through Docker Compose. Container definitions include:

1. **api**: Main application service
   - Port: 8080 (exposed)
   - Health check endpoint: `/health`
   - Depends on: db service

2. **db**: PostgreSQL database service  
   - Port: 5432 (exposed)
   - Environment variables for credentials
   - Data persistence using volumes
   - Standard PostgreSQL health checks

3. **web**: Web application build and serving
   - Port: 3000 (exposed for development)
   - Development server health monitoring
   - Build process integrated with Docker

### Health Checks
Each service includes appropriate health check endpoints:
- API service: `/health`
- Database: PostgreSQL standard health checks
- Web: Built-in React development server health monitoring

## Repository Structure
```
.
├── src/
│   ├── backend/               # Backend code
│   ├── web/                   # Frontend code
│   └── tests/                 # Test projects
├── docs/
│   ├── adr/                   # Architecture Decision Records  
│   └── specs/                 # Platform specifications
├── .github/
│   └── workflows/             # CI/CD pipelines
└── docker-compose.yml         # Container orchestration
```

## Technical Requirements

### Code Quality
- Follow .NET coding standards and patterns
- Maintain consistent naming conventions
- Adhere to SOLID principles in object-oriented design
- Use dependency injection for better testability

### Security Considerations
- Input validation at all API endpoints
- Secure database connection management
- Token-based authentication (to be implemented)
- HTTPS enforcement in production deployments

### Performance
- Database indexes on frequently queried fields
- Efficient query patterns with EF Core
- Caching strategy where appropriate
- Asynchronous programming patterns for I/O-bound operations

## Future Considerations
This baseline provides the foundation for future features and enhancements. As we move forward, additional decisions may be documented through new ADRs.
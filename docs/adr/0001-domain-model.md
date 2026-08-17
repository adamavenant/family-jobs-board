# 0001 - Domain Model

## Status

Accepted

## Context

We need to establish a clear and consistent domain model for the Family Jobs Board application to ensure all team members understand the core concepts, relationships, and constraints within our system.

## Decision

We have defined the core domain concepts for the Family Jobs Board application with clear definitions and relationships between them:

- User: The generic term for anyone using the application
- Child: A specific type of User who is a minor
- Adult: A specific type of User with administrative privileges 
- Job: Tasks assigned to users, with approval process before points are awarded
- Job Approval: The verification mechanism for completed jobs
- Good Behaviour: Positive actions logged by adults that earn points
- Good Behaviour Type: Pre-defined categories of good behaviours
- Points: The currency accumulated by children for completing tasks and good behaviours

## Consequences

### Positive

1. **Clarity**: All team members now have a shared understanding of the core concepts
2. **Consistency**: Clear definitions prevent ambiguity in development and documentation
3. **Relationships**: Well-defined relationships between concepts guide system design decisions
4. **Maintainability**: Clear domain model makes it easier to modify and extend features

### Negative

1. **Learning Curve**: Team members need time to familiarize themselves with the new terminology
2. **Documentation Updates**: Existing documentation needs to be updated to align with these definitions

### Neutral

1. **Implementation Guidance**: The model serves as a reference for implementing features and solving design problems
2. **Future ADRs**: This domain model will provide context for future architectural decisions
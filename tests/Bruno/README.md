# Bruno API Tests for Sanad API

This directory contains Bruno test collections for testing the newly implemented family and caregiver help request endpoints.

## Test Structure

- `family/dependents/check-ins/` - Tests for family dependent check-ins endpoints
- `family/dependents/help-requests/` - Tests for family dependent help requests endpoints  
- `caregiver/help-requests/` - Tests for caregiver help requests endpoints

## Test Files

### Family Dependent Check-ins
- `get-check-ins-today.bruno` - GET /dependents/{dependentId}/check-ins/today
- `get-check-ins.bruno` - GET /dependents/{dependentId}/check-ins (with pagination/filtering)

### Family Dependent Help Requests
- `get-help-requests.bruno` - GET /dependents/{dependentId}/help-requests (with pagination/filtering)
- `get-help-request-by-id.bruno` - GET /dependents/{dependentId}/help-requests/{requestId}

### Caregiver Help Requests
- `get-help-requests.bruno` - GET /help-requests (with pagination/filtering)
- `get-help-request-by-id.bruno` - GET /help-requests/{requestId}

## Setup

1. Copy `bruno.env.template` to `bruno.env` and update the values:
   - `accessToken`: Obtain a valid JWT token through the authentication endpoints
   - `dependentId`: ID of a test dependent/elderly person
   - `requestId`: ID of a test help request
   - `elderlyId`: ID of a test elderly person (for caregiver tests)
   - Update pagination and filter values as needed

2. Ensure the Sanad API is running and accessible at the configured base URL (default: http://localhost:5000)

## Running Tests

You can run these tests using the Bruno CLI or through the Bruno application:

### Bruno CLI
```bash
bruno run tests/bruno/
```

### Bruno Application
1. Open Bruno application
2. Import the tests/bruno directory as a collection
3. Select the environment
4. Run individual tests or the entire collection

## Test Coverage

These tests verify:
- Endpoint accessibility with proper authentication
- Correct response formats and status codes
- Pagination functionality (page, pageSize)
- Filtering capabilities (date ranges, status, categorical filters)
- Authorization boundaries (family members only see their dependents, caregivers only see requests from elderly people with active bookings)
- Error handling (invalid IDs, unauthorized access, etc.)
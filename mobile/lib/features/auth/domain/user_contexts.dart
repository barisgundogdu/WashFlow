/// Supported tenant grants. Unknown backend role names never become a grant.
enum BusinessRole { owner, employee }

class CustomerContext {
  const CustomerContext._(this.customerProfileId);
  final String customerProfileId;

  factory CustomerContext.fromJson(Map<String, dynamic> json) =>
      CustomerContext._(_id(json['customerProfileId']));
}

class BusinessContext {
  BusinessContext._({
    required this.businessId,
    required this.businessName,
    required this.membershipId,
    required Set<BusinessRole> roles,
  }) : roles = Set.unmodifiable(roles);

  final String businessId;
  final String businessName;
  final String membershipId;
  final Set<BusinessRole> roles;

  factory BusinessContext.fromJson(Map<String, dynamic> json) {
    final name = json['businessName'];
    final rawRoles = json['roles'];
    if (name is! String || name.trim().isEmpty || rawRoles is! List) {
      throw const FormatException('Invalid business context.');
    }
    final roles = <BusinessRole>{};
    for (final role in rawRoles) {
      if (role is! String) {
        throw const FormatException('Invalid business role.');
      }
      switch (role) {
        case 'Owner':
          roles.add(BusinessRole.owner);
        case 'Employee':
          roles.add(BusinessRole.employee);
        default:
          break;
      }
    }
    return BusinessContext._(
      businessId: _id(json['businessId']),
      businessName: name,
      membershipId: _id(json['membershipId']),
      roles: roles,
    );
  }
}

/// A snapshot returned by the backend, not a client-side authorization decision.
class UserContexts {
  UserContexts._(this.customerContext, List<BusinessContext> businesses)
    : businessContexts = List.unmodifiable(businesses);

  final CustomerContext? customerContext;
  final List<BusinessContext> businessContexts;

  factory UserContexts.fromJson(Map<String, dynamic> json) {
    final customer = json['customerContext'];
    final businesses = json['businessContexts'];
    if (!json.containsKey('customerContext') ||
        (customer != null && customer is! Map<String, dynamic>) ||
        businesses is! List) {
      throw const FormatException('Invalid user contexts.');
    }
    final parsed = <BusinessContext>[];
    final businessIds = <String>{};
    final membershipIds = <String>{};
    for (final business in businesses) {
      if (business is! Map<String, dynamic>) {
        throw const FormatException('Invalid business context.');
      }
      final context = BusinessContext.fromJson(business);
      if (!businessIds.add(context.businessId) ||
          !membershipIds.add(context.membershipId)) {
        throw const FormatException('Duplicate business context.');
      }
      parsed.add(context);
    }
    return UserContexts._(
      customer == null
          ? null
          : CustomerContext.fromJson(customer as Map<String, dynamic>),
      parsed,
    );
  }
}

String _id(Object? value) {
  if (value is! String ||
      !RegExp(
        r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$',
      ).hasMatch(value) ||
      value == '00000000-0000-0000-0000-000000000000') {
    throw const FormatException('Invalid context identifier.');
  }
  return value.toLowerCase();
}

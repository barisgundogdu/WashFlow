import 'dart:convert';
import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:washflow/core/network/api_client.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/auth/domain/session_service.dart';
import 'package:washflow/features/scheduling/data/api_scheduling_service.dart';
import 'package:washflow/features/scheduling/domain/scheduling.dart';

import 'support/scheduling_fakes.dart';
import 'support/session_fakes.dart';
import 'support/catalog_fakes.dart' as catalog;

class Tokens implements AccessTokenProvider {
  @override
  Future<AuthResult<String>> accessToken() async =>
      const AuthSuccess('fixture');
}

class IdentityTokens extends FakeSession implements AccessTokenProvider {
  Future<AuthResult<String>> Function()? handler;
  @override
  Future<AuthResult<String>> accessToken() async =>
      handler == null ? const AuthSuccess('fixture') : handler!();
}

void main() {
  test(
    'identity change during token retrieval prevents sending request',
    () async {
      final session = IdentityTokens();
      addTearDown(session.dispose);
      final pending = Completer<AuthResult<String>>();
      session.handler = () => pending.future;
      var calls = 0;
      final service = ApiSchedulingService(
        session: session,
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((_) async {
            calls++;
            return http.Response('[]', 200);
          }),
        ),
      );
      final result = service.areas(businessA);
      session.changeIdentity();
      pending.complete(const AuthSuccess('old-fixture'));
      expect(await result, isA<AuthFailure<List<WorkArea>>>());
      expect(calls, 0);
    },
  );
  test(
    'identity change while HTTP is pending discards old account response',
    () async {
      final session = IdentityTokens();
      addTearDown(session.dispose);
      final sent = Completer<void>(), response = Completer<http.Response>();
      final service = ApiSchedulingService(
        session: session,
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((_) {
            sent.complete();
            return response.future;
          }),
        ),
      );
      final result = service.areas(businessA);
      await sent.future;
      session.changeIdentity();
      response.complete(
        http.Response(
          jsonEncode([areaJson()]),
          200,
          headers: {'content-type': 'application/json; charset=utf-8'},
        ),
      );
      final error = ((await result) as AuthFailure<List<WorkArea>>).error;
      expect(error.code, AuthErrorCode.unauthenticated);
    },
  );
  final base = '/api/v1/businesses/$businessA';
  late List<http.Request> requests;
  late ApiSchedulingService s;
  late int status;
  late Object response;
  setUp(() {
    requests = [];
    status = 200;
    response = appointmentJson();
    s = ApiSchedulingService(
      apiClient: ApiClient(
        baseUrl: Uri.parse('https://example.test'),
        client: MockClient((r) async {
          requests.add(r);
          return http.Response(
            jsonEncode(response),
            status,
            headers: {'content-type': 'application/json; charset=utf-8'},
          );
        }),
      ),
      session: Tokens(),
    );
  });
  test(
    'create 201 and duplicate 200 parse direct response with stable UUID body',
    () async {
      final attempt = BookingAttempt(bookingPayload());
      status = 201;
      expect(
        await s.create(businessA, attempt),
        isA<AuthSuccess<Appointment>>(),
      );
      status = 200;
      expect(
        await s.create(businessA, attempt),
        isA<AuthSuccess<Appointment>>(),
      );
      expect(requests.map((r) => r.body).toSet(), hasLength(1));
      expect(requests.first.url.path, '$base/appointments');
      expect(jsonDecode(requests.first.body), attempt.toJson());
      expect(requests.first.headers['Authorization'], 'Bearer fixture');
      expect(requests.first.headers.keys, isNot(contains('Idempotency-Key')));
    },
  );
  test('calendar uses bounded local date range and array response', () async {
    response = [appointmentJson()];
    expect(
      await s.calendar(
        businessA,
        DateTime.utc(2030, 1, 2),
        DateTime.utc(2030, 1, 9),
        page: 2,
      ),
      isA<AuthSuccess<List<Appointment>>>(),
    );
    expect(requests.single.url.queryParameters, {
      'fromDate': '2030-01-02',
      'toDateExclusive': '2030-01-09',
      'page': '2',
      'pageSize': '50',
    });
    expect(
      await s.calendar(
        businessA,
        DateTime.utc(2030, 1, 1),
        DateTime.utc(2030, 3, 1),
      ),
      isA<AuthFailure<List<Appointment>>>(),
    );
    expect(requests, hasLength(1));
  });
  test(
    'detail reschedule cancellation echo opaque version and exact routes',
    () async {
      final a = Appointment.fromJson(appointmentJson(version: 4294967295));
      expect(
        await s.detail(businessA, appointmentId),
        isA<AuthSuccess<Appointment>>(),
      );
      expect(
        await s.reschedule(businessA, a, bookingPayload().schedule),
        isA<AuthSuccess<Appointment>>(),
      );
      expect(await s.cancel(businessA, a), isA<AuthSuccess<Appointment>>());
      expect(requests.map((r) => r.method), ['GET', 'POST', 'POST']);
      expect(requests.map((r) => r.url.path), [
        '$base/appointments/$appointmentId',
        '$base/appointments/$appointmentId/reschedule',
        '$base/appointments/$appointmentId/cancel',
      ]);
      expect(jsonDecode(requests[1].body), {
        'version': 4294967295,
        ...bookingPayload().schedule.toJson(),
      });
      expect(jsonDecode(requests[2].body), {'version': 4294967295});
    },
  );
  test('preview uses actual contract with no reservation token', () async {
    response = {
      'available': false,
      'startsAtUtc': '2030-01-02T07:00:00Z',
      'activeEndsAtUtc': '2030-01-02T07:20:00Z',
    };
    final result = await s.preview(
      businessA,
      catalog.serviceId,
      'Car',
      bookingPayload().schedule,
    );
    expect(
      (result as AuthSuccess<AvailabilityPreview>).value.available,
      isFalse,
    );
    expect(jsonDecode(requests.single.body), {
      'serviceId': catalog.serviceId,
      'category': 'Car',
      ...bookingPayload().schedule.toJson(),
    });
  });
  test('area create/update/archive and validation', () async {
    response = areaJson();
    status = 201;
    expect(
      await s.saveArea(businessA, const WorkAreaInput('Alan', 3)),
      isA<AuthSuccess<WorkArea>>(),
    );
    status = 200;
    expect(
      await s.saveArea(
        businessA,
        const WorkAreaInput('Alan', 4, version: 7),
        id: areaId,
      ),
      isA<AuthSuccess<WorkArea>>(),
    );
    status = 204;
    expect(
      await s.archiveArea(businessA, WorkArea.fromJson(areaJson())),
      isA<AuthSuccess<void>>(),
    );
    expect(requests.map((r) => r.method), ['POST', 'PUT', 'POST']);
    expect(requests.last.url.path, '$base/work-areas/$areaId/archive');
    expect(jsonDecode(requests[1].body), {
      'name': 'Alan',
      'capacity': 4,
      'version': 7,
    });
    expect(
      await s.saveArea(businessA, const WorkAreaInput('Alan', 0)),
      isA<AuthFailure<WorkArea>>(),
    );
    expect(requests, hasLength(3));
  });
  test('requirements preserve confirmation/version and actual disable action', () async {
    response = [requirementJson()];
    expect(
      await s.requirements(businessA, catalog.serviceId),
      isA<AuthSuccess<List<SchedulingRequirement>>>(),
    );
    response = requirementJson();
    expect(
      await s.saveRequirement(
        businessA,
        catalog.serviceId,
        'Car',
        const RequirementInput(areaId, true, version: 8),
      ),
      isA<AuthSuccess<SchedulingRequirement>>(),
    );
    expect(
      await s.disableRequirement(
        businessA,
        SchedulingRequirement.fromJson(requirementJson()),
      ),
      isA<AuthSuccess<SchedulingRequirement>>(),
    );
    expect(
      requests[1].url.path,
      '$base/services/${catalog.serviceId}/offerings/Car/scheduling-requirement',
    );
    expect(jsonDecode(requests[1].body), {
      'workAreaId': areaId,
      'singleStageConfirmed': true,
      'version': 8,
    });
    expect(requests.last.url.path, endsWith('/scheduling-requirement/disable'));
  });
  test('availability list/create/update/archive serialize UTC instants and versions', () async {
    response = [availabilityJson()];
    expect(
      await s.availability(
        businessA,
        employeeId,
        DateTime.utc(2030, 1, 2),
        DateTime.utc(2030, 1, 9),
      ),
      isA<AuthSuccess<List<EmployeeAvailability>>>(),
    );
    expect(
      requests.single.url.queryParameters['fromUtc'],
      '2030-01-02T00:00:00.000Z',
    );
    final input = AvailabilityInput(
      DateTime.parse('2030-01-02T09:00:00+03:00'),
      DateTime.parse('2030-01-02T18:00:00+03:00'),
      version: 9,
    );
    response = availabilityJson();
    status = 201;
    expect(
      await s.saveAvailability(businessA, employeeId, input),
      isA<AuthSuccess<EmployeeAvailability>>(),
    );
    status = 200;
    expect(
      await s.saveAvailability(
        businessA,
        employeeId,
        input,
        id: availabilityId,
      ),
      isA<AuthSuccess<EmployeeAvailability>>(),
    );
    status = 204;
    expect(
      await s.archiveAvailability(
        businessA,
        EmployeeAvailability.fromJson(availabilityJson()),
      ),
      isA<AuthSuccess<void>>(),
    );
    expect(
      jsonDecode(requests[1].body)['startsAtUtc'],
      '2030-01-02T06:00:00.000Z',
    );
    expect(requests[2].method, 'PUT');
    expect(requests.last.url.path, endsWith('/$availabilityId/archive'));
  });
  for (final entry in {
    400: AuthErrorCode.validation,
    401: AuthErrorCode.unauthenticated,
    403: AuthErrorCode.forbidden,
    404: AuthErrorCode.notFound,
    409: AuthErrorCode.conflict,
  }.entries) {
    test('${entry.key} safe errors with no automatic mutation retry', () async {
      status = entry.key;
      response = {
        'code': 'resourceConflict',
        'detail': 'private database password',
      };
      final result = await s.create(
        businessA,
        BookingAttempt(bookingPayload()),
      );
      final error = (result as AuthFailure<Appointment>).error;
      expect(error.code, entry.value);
      expect(schedulingError(error), isNot(contains('password')));
      expect(requests, hasLength(1));
    });
  }
  test(
    'foreign business and employee response identities are rejected',
    () async {
      response = {...appointmentJson(), 'businessId': businessB};
      expect(
        await s.detail(businessA, appointmentId),
        isA<AuthFailure<Appointment>>(),
      );
      response = [
        {...availabilityJson(), 'membershipId': areaId},
      ];
      expect(
        await s.availability(
          businessA,
          employeeId,
          DateTime.utc(2030),
          DateTime.utc(2030, 1, 2),
        ),
        isA<AuthFailure<List<EmployeeAvailability>>>(),
      );
    },
  );
  test('roster and area lists use authenticated tenant routes', () async {
    response = [employeeJson()];
    expect(
      await s.employees(businessA),
      isA<AuthSuccess<List<SchedulingEmployee>>>(),
    );
    response = [areaJson()];
    expect(await s.areas(businessA), isA<AuthSuccess<List<WorkArea>>>());
    expect(requests.map((r) => r.url.path), [
      '$base/scheduling-employees',
      '$base/work-areas',
    ]);
  });
}

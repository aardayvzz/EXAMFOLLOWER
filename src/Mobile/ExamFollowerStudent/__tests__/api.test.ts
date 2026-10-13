import {ApiClient} from '../src/api';

describe('ApiClient authentication behavior', () => {
  test('clears the session callback on 401 without exposing a server body', async () => {
    const onUnauthorized = jest.fn();
    const fetchImpl = jest.fn().mockResolvedValue({
      status: 401,
      ok: false,
      json: jest.fn(),
    });
    const client = new ApiClient({
      baseUrl: 'https://api.example.test',
      getToken: () => 'secret-token-for-test-only',
      onUnauthorized,
      fetchImpl: fetchImpl as unknown as typeof fetch,
    });

    await expect(client.upcomingLessons()).rejects.toMatchObject({status: 401});
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
    expect(fetchImpl).toHaveBeenCalledWith(
      'https://api.example.test/api/lesson-notifications/upcoming?days=30',
      expect.objectContaining({
        headers: expect.objectContaining({
          Authorization: 'Bearer secret-token-for-test-only',
        }),
      }),
    );
  });
});

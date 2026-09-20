import { useGetMeQuery } from "../auth/authApi";

/** The signed-in view at `/`, until the landing page and its ticker box take its place. */
export function HomePage() {
  const { data: me } = useGetMeQuery();

  return (
    <main>
      <h1>Your projections</h1>
      <p className="note">Signed in as {me?.email}</p>
    </main>
  );
}

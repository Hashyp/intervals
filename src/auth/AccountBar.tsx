import { useAuth } from "./AuthProvider";

export function AccountBar() {
  const { session, signOut } = useAuth();
  if (!session) {
    return null;
  }
  return (
    <div className="account-bar">
      <span className="account-bar__name">
        Signed in as <strong>{session.user.displayName}</strong>
      </span>
      <button
        type="button"
        className="account-bar__signout"
        onClick={() => void signOut()}
      >
        Sign out
      </button>
    </div>
  );
}

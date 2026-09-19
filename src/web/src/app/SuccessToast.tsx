import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useState,
} from "react";
import type { ReactNode } from "react";

interface SuccessToastContextValue {
  showSuccess: (message: string) => void;
}

const SuccessToastContext = createContext<SuccessToastContextValue | null>(
  null,
);

export function SuccessToastProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState<string>();
  const showSuccess = useCallback((nextMessage: string) => {
    setMessage(nextMessage);
  }, []);
  const value = useMemo(() => ({ showSuccess }), [showSuccess]);

  return (
    <SuccessToastContext.Provider value={value}>
      {children}
      {message ? (
        <div className="status-toast">
          <span role="status">{message}</span>
          <button
            type="button"
            onClick={() => setMessage(undefined)}
            aria-label="Dismiss notification"
          >
            ×
          </button>
        </div>
      ) : null}
    </SuccessToastContext.Provider>
  );
}

export function useSuccessToast(): SuccessToastContextValue {
  const value = useContext(SuccessToastContext);
  if (!value) {
    throw new Error(
      "useSuccessToast must be used within SuccessToastProvider.",
    );
  }

  return value;
}

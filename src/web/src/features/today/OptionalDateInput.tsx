import { useRef } from "react";

/**
 * A date input that can be emptied again. Native date pickers cannot reliably
 * clear a value: Safari's "Reset" restores the saved date and Chrome shows no
 * clear control, so an optional date needs its own clear button.
 */
export function OptionalDateInput({
  id,
  name,
  value,
  min,
  fieldName,
  onChange,
}: {
  id: string;
  name: string;
  value: string;
  min?: string;
  fieldName: string;
  onChange: (value: string) => void;
}) {
  const inputRef = useRef<HTMLInputElement>(null);

  return (
    <div className="optional-date">
      <input
        ref={inputRef}
        id={id}
        name={name}
        type="date"
        min={min}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
      {value ? (
        <button
          type="button"
          className="optional-date__clear"
          aria-label={`Clear ${fieldName}`}
          onClick={() => {
            onChange("");
            inputRef.current?.focus();
          }}
        >
          Clear
        </button>
      ) : null}
    </div>
  );
}

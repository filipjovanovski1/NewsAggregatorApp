interface Props {
    country: string;
}

const Star = ({ x, y, scale = 1 }: { x: number; y: number; scale?: number }) => (
    <path
        d="M0-3 0.7-1 2.9-0.9 1.2 0.4 1.8 2.5 0 1.3-1.8 2.5-1.2 0.4-2.9-0.9-0.7-1Z"
        transform={`translate(${x} ${y}) scale(${scale})`}
    />
);

export default function FlagIcon({ country }: Props) {
    const commonProps = {
        className: 'language-flag',
        viewBox: '0 0 24 16',
        'aria-hidden': true,
    } as const;

    switch (country) {
        case 'CN':
            return <svg {...commonProps}><rect width="24" height="16" fill="#de2910" /><g fill="#ffde00"><Star x={5} y={5} scale={1.05} /><Star x={9} y={2.8} scale={0.42} /><Star x={10.8} y={5.2} scale={0.42} /><Star x={10.4} y={8} scale={0.42} /><Star x={8.2} y={10} scale={0.42} /></g></svg>;
        case 'ES':
            return <svg {...commonProps} viewBox="0 5 36 26"><path fill="#c60a1d" d="M36 27c0 2.209-1.791 4-4 4H4c-2.209 0-4-1.791-4-4V9c0-2.209 1.791-4 4-4h28c2.209 0 4 1.791 4 4v18z" /><path fill="#ffc400" d="M0 12h36v12H0z" /><path fill="#ea596e" d="M9 17v3c0 1.657 1.343 3 3 3s3-1.343 3-3v-3H9z" /><path fill="#f4a2b2" d="M12 16h3v3h-3z" /><path fill="#dd2e44" d="M9 16h3v3H9z" /><ellipse fill="#ea596e" cx="12" cy="14.5" rx="3" ry="1.5" /><ellipse fill="#ffac33" cx="12" cy="13.75" rx="3" ry=".75" /><path fill="#99aab5" d="M7 16h1v7H7zm9 0h1v7h-1z" /><path fill="#66757f" d="M6 22h3v1H6zm9 0h3v1h-3zm-8-7h1v1H7zm9 0h1v1h-1z" /></svg>;
        case 'GB':
            return <svg {...commonProps}><rect width="24" height="16" fill="#012169" /><path d="M0 0 24 16M24 0 0 16" stroke="#fff" strokeWidth="4" /><path d="M0 0 24 16M24 0 0 16" stroke="#c8102e" strokeWidth="1.7" /><path d="M12 0v16M0 8h24" stroke="#fff" strokeWidth="5" /><path d="M12 0v16M0 8h24" stroke="#c8102e" strokeWidth="2.7" /></svg>;
        case 'IN':
            return <svg {...commonProps}><rect width="24" height="16" fill="#fff" /><rect width="24" height="5.33" fill="#ff9933" /><rect y="10.67" width="24" height="5.33" fill="#138808" /><circle cx="12" cy="8" r="2.1" fill="none" stroke="#000080" strokeWidth="0.65" /><circle cx="12" cy="8" r="0.45" fill="#000080" /></svg>;
        case 'PT':
            return <svg {...commonProps}><rect width="9.5" height="16" fill="#046a38" /><rect x="9.5" width="14.5" height="16" fill="#da291c" /><circle cx="9.5" cy="8" r="3" fill="#ffcc29" /><rect x="8.35" y="5.8" width="2.3" height="4.4" rx="0.5" fill="#fff" stroke="#da291c" strokeWidth="0.55" /></svg>;
        case 'BD':
            return <svg {...commonProps}><rect width="24" height="16" fill="#006a4e" /><circle cx="10.5" cy="8" r="4.2" fill="#f42a41" /></svg>;
        case 'RU':
            return <svg {...commonProps}><rect width="24" height="16" fill="#fff" /><rect y="5.33" width="24" height="5.34" fill="#0039a6" /><rect y="10.67" width="24" height="5.33" fill="#d52b1e" /></svg>;
        case 'JP':
            return <svg {...commonProps}><rect width="24" height="16" fill="#fff" /><circle cx="12" cy="8" r="4.2" fill="#bc002d" /></svg>;
        case 'TR':
            return <svg {...commonProps}><rect width="24" height="16" fill="#e30a17" /><circle cx="9" cy="8" r="4.1" fill="#fff" /><circle cx="10.5" cy="8" r="3.25" fill="#e30a17" /><g fill="#fff"><Star x={15.2} y={8} scale={0.8} /></g></svg>;
        case 'VN':
            return <svg {...commonProps}><rect width="24" height="16" fill="#da251d" /><g fill="#ff0"><Star x={12} y={8} scale={1.65} /></g></svg>;
        case 'SA':
            return <svg {...commonProps}><rect width="24" height="16" fill="#006c35" /><text x="12" y="7.1" fill="#fff" fontFamily="Arial, sans-serif" fontSize="2.75" fontWeight="700" textAnchor="middle">لا إله إلا الله محمد رسول الله</text><path d="M5.1 11.6h12.7c1.2 0 1.7-.55 2.15-1.05-.15 1.4-1.05 2.1-2.5 2.1H5.1z" fill="#fff" /><path d="M18.1 10.7h2.15" stroke="#fff" strokeWidth=".65" strokeLinecap="round" /></svg>;
        case 'KR':
            return <svg {...commonProps} viewBox="0 5 36 26"><path fill="#eee" d="M36 27c0 2.209-1.791 4-4 4H4c-2.209 0-4-1.791-4-4V9c0-2.209 1.791-4 4-4h28c2.209 0 4 1.791 4 4v18z" /><path fill="#c60c30" d="M21.441 13.085c-2.714-1.9-6.455-1.24-8.356 1.474-.95 1.356-.621 3.227.737 4.179 1.357.949 3.228.618 4.178-.738s2.822-1.687 4.178-.736c1.358.95 1.688 2.821.737 4.178 1.901-2.714 1.241-6.455-1.474-8.357z" /><path fill="#003478" d="M22.178 17.264c-1.356-.951-3.228-.62-4.178.736s-2.821 1.687-4.178.737c-1.358-.951-1.687-2.822-.737-4.179-1.901 2.716-1.241 6.456 1.473 8.356 2.715 1.901 6.455 1.242 8.356-1.474.951-1.355.621-3.226-.736-4.176z" /><path fill="#292f33" d="m24.334 25.572 1.928-2.298.766.643-1.928 2.298zm2.57-3.063 1.928-2.297.766.643-1.928 2.297zm-1.038 4.351 1.928-2.297.766.643-1.928 2.297zm2.572-3.066 1.93-2.297.766.644-1.93 2.296zm-1.041 4.352 1.93-2.297.765.643-1.929 2.297zm2.571-3.065 1.927-2.3.767.643-1.927 2.3zm.004-14.162.766-.643 1.93 2.299-.767.643zM27.4 7.853l.766-.643 1.928 2.299-.767.642zm-1.533 1.288.766-.643 4.5 5.362-.766.643zm-1.532 1.284.767-.643 1.927 2.298-.766.642zm2.57 3.065.766-.643 1.93 2.297-.765.643zM6.4 20.854l.766-.643 4.499 5.363-.767.643zM4.87 22.14l.765-.642 1.929 2.298-.767.643zm2.567 3.066.766-.643 1.93 2.297-.766.643zm-4.101-1.781.766-.643 4.5 5.362-.767.643zm-.001-10.852 4.498-5.362.767.642-4.5 5.363zm1.532 1.287 4.5-5.363.766.643-4.5 5.362zM6.4 15.145l4.5-5.363.766.643-4.5 5.363z" /></svg>;
        case 'ID':
            return <svg {...commonProps}><rect width="24" height="8" fill="#ce1126" /><rect y="8" width="24" height="8" fill="#fff" /></svg>;
        case 'DE':
            return <svg {...commonProps}><rect width="24" height="5.33" fill="#000" /><rect y="5.33" width="24" height="5.34" fill="#dd0000" /><rect y="10.67" width="24" height="5.33" fill="#ffce00" /></svg>;
        case 'FR':
            return <svg {...commonProps}><rect width="8" height="16" fill="#0055a4" /><rect x="8" width="8" height="16" fill="#fff" /><rect x="16" width="8" height="16" fill="#ef4135" /></svg>;
        case 'MK':
            return <svg {...commonProps} viewBox="0 5 36 26"><path fill="#d20000" d="M34.618 5.998 32 6l-1.5-1H20l-2 1-2-1H5.5L4 6l-2.618-.002C.542 6.731 0 7.797 0 9v6.5L1 18l-1 2.5V27c0 1.203.542 2.269 1.382 3.002L4 30l1.5 1H16l2-1 2 1h10.5l1.5-1 2.618.002C35.458 29.269 36 28.203 36 27v-6.5L35 18l1-2.5V9c0-1.203-.542-2.269-1.382-3.002z" /><path fill="#ffe600" d="M36 20.5v-5l-13.681 1.9a4.32 4.32 0 0 0-.779-1.957l13.091-9.455A3.985 3.985 0 0 0 32 5h-1.5l-9.663 9.691a4.37 4.37 0 0 0-2.392-1.026L20 5h-4l1.555 8.665a4.37 4.37 0 0 0-2.392 1.026L5.5 5H4a3.985 3.985 0 0 0-2.632.988l13.092 9.455a4.32 4.32 0 0 0-.779 1.957L0 15.5v5l13.681-1.9c.101.724.369 1.391.779 1.957L1.368 30.012C2.072 30.628 2.993 31 4 31h1.5l9.663-9.691a4.37 4.37 0 0 0 2.392 1.026L16 31h4l-1.555-8.665a4.37 4.37 0 0 0 2.392-1.026L30.5 31H32a3.985 3.985 0 0 0 2.632-.988L21.54 20.557a4.32 4.32 0 0 0 .779-1.957L36 20.5z" /><path fill="#d20000" d="M18 13.62A4.385 4.385 0 0 0 13.62 18 4.385 4.385 0 0 0 18 22.38 4.385 4.385 0 0 0 22.38 18 4.385 4.385 0 0 0 18 13.62zm0 7.737A3.36 3.36 0 0 1 14.643 18 3.36 3.36 0 0 1 18 14.643 3.36 3.36 0 0 1 21.357 18 3.36 3.36 0 0 1 18 21.357z" /></svg>;
        default:
            return <svg {...commonProps}><rect width="24" height="16" rx="1" fill="#263a55" /></svg>;
    }
}
